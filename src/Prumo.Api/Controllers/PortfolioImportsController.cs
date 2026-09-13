using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prumo.Application.PortfolioImports;
using Prumo.Domain.Entities;
using Prumo.Domain.Enums;
using Prumo.Domain.Rules;
using Prumo.Infrastructure.Persistence;
using Prumo.Infrastructure.PortfolioImports;
using Prumo.Infrastructure.Reconciliation;
using Prumo.Application.Portfolio;

namespace Prumo.Api.Controllers;

[ApiController]
[Route("api/portfolios/{portfolioId:guid}/imports")]
public sealed class PortfolioImportsController : ControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private readonly PrumoDbContext db;
    private readonly IEnumerable<IPortfolioImporter> importers;
    private readonly NewAssetBootstrapService bootstrap;
    private readonly ILogger<PortfolioImportsController> logger;
    private readonly IWebHostEnvironment environment;
    public PortfolioImportsController(PrumoDbContext db, IEnumerable<IPortfolioImporter> importers, NewAssetBootstrapService bootstrap, TransactionReconciliationService reconciliation, ILogger<PortfolioImportsController> logger, IWebHostEnvironment environment) { this.db = db; this.importers = importers; this.bootstrap = bootstrap; this.reconciliation = reconciliation; this.logger = logger; this.environment = environment; }
    private readonly TransactionReconciliationService reconciliation;

    [HttpPost("b3/preview")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<ImportPreviewDto>> Preview(Guid portfolioId, IFormFile file, CancellationToken ct)
    {
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolioId, ct)) return NotFound();
        if (file is null || file.Length == 0 || file.Length > MaxUploadBytes) return BadRequest("Selecione um arquivo XLSX válido de até 10 MB.");
        await using var stream = file.OpenReadStream();
        var header = new byte[4];
        if (await stream.ReadAsync(header, ct) != 4) return BadRequest("Arquivo vazio ou inválido.");
        var importer = importers.FirstOrDefault(candidate => candidate.CanHandle(file.FileName, file.ContentType, header));
        if (importer is null) return BadRequest("Formato não suportado. Envie o relatório consolidado da B3 em XLSX.");
        stream.Position = 0;
        var normalized = await importer.ParseAsync(stream, ct);
        var hash = Convert.ToHexString(SHA256.HashData(await ReadAllAsync(file, ct))).ToLowerInvariant();
        var completedImport = await db.PortfolioImportSessions.SingleOrDefaultAsync(s => s.PortfolioId == portfolioId && s.Source == PortfolioImportSource.B3File && s.FileHash == hash && s.Status == PortfolioImportStatus.Completed, ct);
        if (completedImport is not null)
        {
            var completedNormalized = JsonSerializer.Deserialize<NormalizedPortfolioImport>(completedImport.NormalizedJson);
            if (completedNormalized is not null) return Ok(await BuildPreview(completedImport, completedNormalized, portfolioId, ct));
        }
        var session = new PortfolioImportSession(portfolioId, PortfolioImportSource.B3File, Path.GetFileName(file.FileName), hash, JsonSerializer.Serialize(normalized), normalized.ReferenceDate);
        db.PortfolioImportSessions.Add(session); await db.SaveChangesAsync(ct);
        return Ok(await BuildPreview(session, normalized, portfolioId, ct));
    }

    [HttpPost("{importSessionId:guid}/confirm")]
    public async Task<ActionResult<ImportConfirmationDto>> Confirm(Guid portfolioId, Guid importSessionId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var session = await db.PortfolioImportSessions.SingleOrDefaultAsync(s => s.Id == importSessionId && s.PortfolioId == portfolioId, ct);
        if (session is null) return NotFound();
        var normalized = JsonSerializer.Deserialize<NormalizedPortfolioImport>(session.NormalizedJson);
        if (normalized is null || normalized.Errors.Count > 0) return Conflict("O preview não pode ser confirmado.");
        if (session.Status == PortfolioImportStatus.Completed)
        {
            var importedTickers = normalized.Items.Select(x => x.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentTickers = await db.PortfolioPositions.Include(x => x.Asset)
                .Where(x => x.PortfolioId == portfolioId)
                .Select(x => x.Asset!.Ticker)
                .ToListAsync(ct);
            if (!importedTickers.Except(currentTickers, StringComparer.OrdinalIgnoreCase).Any())
                return Ok(new ImportConfirmationDto("AlreadyImported", 0, 0, 0));
        }
        if (session.Status != PortfolioImportStatus.Previewed && session.Status != PortfolioImportStatus.Completed)
            return Conflict("A sessão de importação não está disponível para confirmação.");
        var portfolio = await db.Portfolios.Include(p => p.Positions).ThenInclude(p => p.Asset).SingleAsync(p => p.Id == portfolioId, ct);
        var createdAssets = 0; var addedToPortfolio = 0; var updated = 0; var cnpjsRecorded = 0; var addedAssetIds = new List<Guid>();
        foreach (var item in normalized.Items)
        {
            var asset = await db.Assets.SingleOrDefaultAsync(a => a.Ticker == item.Ticker, ct);
            if (asset is null) { asset = new Asset(item.Ticker, item.AssetName, item.AssetType, null, item.AssetClass, item.Cnpj); db.Assets.Add(asset); createdAssets++; if (!string.IsNullOrWhiteSpace(asset.Cnpj)) cnpjsRecorded++; }
            else if (!string.IsNullOrWhiteSpace(item.Cnpj) && string.IsNullOrWhiteSpace(asset.Cnpj)) { asset.SetCnpj(item.Cnpj); cnpjsRecorded++; }
            var position = portfolio.Positions.SingleOrDefault(p => p.AssetId == asset.Id);
            if (position is null) { position = new PortfolioPosition(portfolio.Id, asset.Id, item.Quantity, item.AveragePrice ?? 0, item.CurrentPrice); db.PortfolioPositions.Add(position); portfolio.Positions.Add(position); addedToPortfolio++; addedAssetIds.Add(asset.Id); }
            else { position.Update(item.Quantity, item.AveragePrice ?? position.AveragePrice, item.CurrentPrice ?? position.CurrentPrice); updated++; }
        }
        PortfolioCalculator.RecalculateWeights(portfolio.Positions);
        await db.SaveChangesAsync(ct);
        var assetsByTicker = await db.Assets.Where(a => normalized.Transactions.Select(t => t.Ticker).Contains(a.Ticker)).ToDictionaryAsync(a => a.Ticker, ct);
        var importedTransactions = 0;
        var ignoredDuplicateTransactions = 0;
        var importedFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var existingFingerprints = await db.PortfolioTransactions.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.ImportFingerprint != null)
            .Select(x => x.ImportFingerprint!)
            .ToHashSetAsync(ct);
        foreach (var imported in normalized.Transactions)
        {
            if (!assetsByTicker.TryGetValue(imported.Ticker, out var asset)) continue;
            var transactionAmount = decimal.Round(imported.TotalAmount, 2);
            if (transactionAmount <= 0) continue;
            var fingerprint = PortfolioTransaction.CreateImportFingerprint(portfolioId, asset.Id, imported.Type, imported.OccurredAt, imported.Quantity, imported.UnitPrice, imported.Institution);
            if (!importedFingerprints.Add(fingerprint) || existingFingerprints.Contains(fingerprint)) { ignoredDuplicateTransactions++; continue; }
            var transactionRecord = new PortfolioTransaction(portfolioId, imported.Type, imported.OccurredAt, null, transactionAmount, fingerprint, FinancialSource.B3Import, imported.DatePrecision);
            transactionRecord.Items.Add(new PortfolioTransactionItem(transactionRecord.Id, asset.Id, imported.Quantity, imported.UnitPrice));
            db.PortfolioTransactions.Add(transactionRecord);
            importedTransactions++;
        }
        session.MarkConfirmed(); session.SetPortfolioSnapshotKey(PortfolioSnapshotKeyCalculator.Calculate(portfolio)); session.MarkCompleted(); await db.SaveChangesAsync(ct);
        var autoReconciled = await reconciliation.AutoResolveDeterministic(portfolioId, ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Import confirmed: ImportSessionId={ImportSessionId}, PortfolioId={PortfolioId}, CreatedAssetCount={CreatedAssetCount}, AddedToPortfolioAssetCount={AddedToPortfolioAssetCount}, UpdatedPositionCount={UpdatedPositionCount}", session.Id, portfolioId, createdAssets, addedToPortfolio, updated);
        try { await bootstrap.DiscoverLatestOnlyAsync(addedAssetIds, ct); } catch (Exception exception) { logger.LogError(exception, "Falha no discovery pós-importação: ImportSessionId={ImportSessionId}, PortfolioId={PortfolioId}", session.Id, portfolioId); }
        var importedDocumentAssetIds = portfolio.Positions
            .Where(position => position.Quantity > 0 && position.Asset?.AssetClass is (AssetClass.Fii or AssetClass.BrazilianStock))
            .Select(position => position.AssetId)
            .Distinct()
            .ToList();
        try
        {
            await bootstrap.DiscoverLatestOnlyAsync(importedDocumentAssetIds, ct);
            logger.LogInformation("Discovery pós-importação concluído: ImportSessionId={ImportSessionId}, PortfolioId={PortfolioId}, AssetCount={AssetCount}", session.Id, portfolioId, importedDocumentAssetIds.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no discovery pós-importação: ImportSessionId={ImportSessionId}, PortfolioId={PortfolioId}", session.Id, portfolioId);
        }
        return Ok(new ImportConfirmationDto("Completed", createdAssets, addedToPortfolio, updated, importedTransactions, ignoredDuplicateTransactions, autoReconciled, cnpjsRecorded));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ImportSessionDto>>> History(Guid portfolioId, CancellationToken ct)
        => Ok(await db.PortfolioImportSessions.AsNoTracking().Where(s => s.PortfolioId == portfolioId).OrderByDescending(s => s.CreatedAt).Take(20).Select(s => new ImportSessionDto(s.Id, s.Source, s.OriginalFileName, s.Status, s.CreatedAt, s.CompletedAt)).ToListAsync(ct));

    [HttpPost("/api/portfolios/{portfolioId:guid}/document-bootstrap/ensure")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<ActionResult<BootstrapRepairResult>> EnsureDocumentBootstrap(Guid portfolioId, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) return NotFound();
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolioId, ct)) return NotFound();
        return Ok(await bootstrap.EnsurePortfolioDocumentBootstrapAsync(portfolioId, ct));
    }

    private async Task<ImportPreviewDto> BuildPreview(PortfolioImportSession session, NormalizedPortfolioImport normalized, Guid portfolioId, CancellationToken ct)
    {
        var existing = await db.PortfolioPositions.Include(p => p.Asset).Where(p => p.PortfolioId == portfolioId).ToListAsync(ct);
        var newItems = normalized.Items.Where(i => existing.All(p => p.Asset?.Ticker != i.Ticker)).Select(i => i.Ticker).ToList();
        var updated = normalized.Items.Where(i => existing.Any(p => p.Asset?.Ticker == i.Ticker)).Select(i => i.Ticker).ToList();
        return new(session.Id, normalized.Source, normalized.ReferenceDate, newItems, updated, [], normalized.Warnings, normalized.Errors, normalized.Errors.Count == 0 && normalized.Items.Count > 0);
    }
    private static async Task<byte[]> ReadAllAsync(IFormFile file, CancellationToken ct) { await using var stream = file.OpenReadStream(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory, ct); return memory.ToArray(); }
}

public sealed record ImportPreviewDto(Guid ImportSessionId, PortfolioImportSource Source, DateTimeOffset? ReferenceDate, IReadOnlyList<string> NewAssets, IReadOnlyList<string> UpdatedPositions, IReadOnlyList<string> UnchangedPositions, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, bool CanConfirm);
public sealed record ImportConfirmationDto(string Status, int CreatedAssets, int AddedToPortfolioAssets, int UpdatedPositions, int ImportedTransactions = 0, int IgnoredDuplicateTransactions = 0, int AutoReconciledTransactions = 0, int CnpjsRecorded = 0);
public sealed record ImportSessionDto(Guid Id, PortfolioImportSource Source, string OriginalFileName, PortfolioImportStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
