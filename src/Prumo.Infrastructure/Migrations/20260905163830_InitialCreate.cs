using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prumo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Ticker = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssetType = table.Column<int>(type: "integer", nullable: false),
                    AssetClass = table.Column<int>(type: "integer", nullable: false),
                    Sector = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SectorCategory = table.Column<int>(type: "integer", nullable: true),
                    ListingCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    TradingCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    GeographicExposureCoverage = table.Column<int>(type: "integer", nullable: false),
                    EconomicCurrencyExposureCoverage = table.Column<int>(type: "integer", nullable: false),
                    Cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContributionGuidanceJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributionGuidanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidatesJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionGuidanceJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContributionGuidances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    PortfolioSnapshotKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetAllocationKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IntelligenceContextKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EngineVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IntelligenceAvailability = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reasoning = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    MetadataJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionGuidances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioImportSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedJson = table.Column<string>(type: "character varying(5000000)", maxLength: 5000000, nullable: false),
                    ReferenceDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PortfolioSnapshotKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioImportSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Portfolios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portfolios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetCorporateActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    FactorNumerator = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    FactorDenominator = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCorporateActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCorporateActions_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetCurrencyExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDerived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCurrencyExposures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCurrencyExposures_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetFundamentalSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodType = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    NetIncome = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Equity = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    TotalDebt = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Cash = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Ebitda = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Roe = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    Roic = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    NetMargin = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetFundamentalSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetFundamentalSnapshots_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetGeographicExposures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDerived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetGeographicExposures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetGeographicExposures_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Close = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetPrices_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetValuationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceEarnings = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    PriceBook = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    EvEbitda = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true),
                    DividendYield = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetValuationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetValuationSnapshots_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceIdentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceCategory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceSpecies = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DownloadUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FileHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionGuidanceAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributionGuidanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Origin = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CandidateKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Ticker = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AssetClass = table.Column<int>(type: "integer", nullable: true),
                    AssetType = table.Column<int>(type: "integer", nullable: true),
                    Exchange = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Currency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Sector = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    EvidenceJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionGuidanceAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContributionGuidanceAllocations_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContributionGuidanceAllocations_ContributionGuidances_Contr~",
                        column: x => x.ContributionGuidanceId,
                        principalTable: "ContributionGuidances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinancialHistoryCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Dimension = table.Column<int>(type: "integer", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialHistoryCoverages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialHistoryCoverages_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialHistoryCoverages_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioCashAnchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioCashAnchors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioCashAnchors_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioCashFlows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioCashFlows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioCashFlows_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioCashFlows_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioHistoricalAnchors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioHistoricalAnchors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoricalAnchors_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    AveragePrice = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    CurrentValue = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true),
                    PortfolioWeight = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioPositions_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioPositions_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ContributionGuidanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactions_ContributionGuidances_ContributionGui~",
                        column: x => x.ContributionGuidanceId,
                        principalTable: "ContributionGuidances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactions_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TargetAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetClass = table.Column<int>(type: "integer", nullable: false),
                    TargetPercentage = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TargetAllocations_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionReconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionReconciliations_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Importance = table.Column<int>(type: "integer", nullable: false),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    RequiresAction = table.Column<bool>(type: "boolean", nullable: false),
                    SuggestedAction = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Impact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    PortfolioImpact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AffectedAssets = table.Column<string>(type: "jsonb", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    RawResponse = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentAnalyses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentFinancialFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Metric = table.Column<int>(type: "integer", nullable: false),
                    ReferencePeriod = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodType = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(24,8)", precision: 24, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFinancialFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentFinancialFacts_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentFinancialFacts_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioHistoricalAnchorItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnchorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    AverageCost = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioHistoricalAnchorItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoricalAnchorItems_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioHistoricalAnchorItems_PortfolioHistoricalAnchors_A~",
                        column: x => x.AnchorId,
                        principalTable: "PortfolioHistoricalAnchors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioTransactionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioTransactionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactionItems_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioTransactionItems_PortfolioTransactions_PortfolioTr~",
                        column: x => x.PortfolioTransactionId,
                        principalTable: "PortfolioTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransactionReconciliationMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioTransactionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionReconciliationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionReconciliationMembers_PortfolioTransactions_Port~",
                        column: x => x.PortfolioTransactionId,
                        principalTable: "PortfolioTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionReconciliationMembers_TransactionReconciliations~",
                        column: x => x.ReconciliationId,
                        principalTable: "TransactionReconciliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Facts = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentEvents_DocumentAnalyses_DocumentAnalysisId",
                        column: x => x.DocumentAnalysisId,
                        principalTable: "DocumentAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RadarItemStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarItemStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadarItemStates_DocumentAnalyses_DocumentAnalysisId",
                        column: x => x.DocumentAnalysisId,
                        principalTable: "DocumentAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentEventAssets",
                columns: table => new
                {
                    AffectedAssetsId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentEventId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentEventAssets", x => new { x.AffectedAssetsId, x.InvestmentEventId });
                    table.ForeignKey(
                        name: "FK_InvestmentEventAssets_Assets_AffectedAssetsId",
                        column: x => x.AffectedAssetsId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvestmentEventAssets_InvestmentEvents_InvestmentEventId",
                        column: x => x.InvestmentEventId,
                        principalTable: "InvestmentEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestmentEventEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentEventEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentEventEvidence_DocumentAnalyses_DocumentAnalysisId",
                        column: x => x.DocumentAnalysisId,
                        principalTable: "DocumentAnalyses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentEventEvidence_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentEventEvidence_InvestmentEvents_InvestmentEventId",
                        column: x => x.InvestmentEventId,
                        principalTable: "InvestmentEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvestmentEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttentionPriority = table.Column<int>(type: "integer", nullable: false),
                    ActionRequirement = table.Column<int>(type: "integer", nullable: false),
                    CapitalDecisionImpact = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UserState = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    WhyItMatters = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SuggestedBehavior = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    TargetAllocationKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssessmentVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PortfolioSnapshotKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InvestmentEventVersion = table.Column<int>(type: "integer", nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioAssessments_InvestmentEvents_InvestmentEventId",
                        column: x => x.InvestmentEventId,
                        principalTable: "InvestmentEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PortfolioAssessments_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCorporateActions_AssetId_EffectiveDate",
                table: "AssetCorporateActions",
                columns: new[] { "AssetId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCorporateActions_AssetId_Type_EffectiveDate_FactorNume~",
                table: "AssetCorporateActions",
                columns: new[] { "AssetId", "Type", "EffectiveDate", "FactorNumerator", "FactorDenominator", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetCurrencyExposures_AssetId_CurrencyCode",
                table: "AssetCurrencyExposures",
                columns: new[] { "AssetId", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetFundamentalSnapshots_AssetId_Source_ReferenceDate_Peri~",
                table: "AssetFundamentalSnapshots",
                columns: new[] { "AssetId", "Source", "ReferenceDate", "PeriodType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetGeographicExposures_AssetId_CountryCode",
                table: "AssetGeographicExposures",
                columns: new[] { "AssetId", "CountryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetPrices_AssetId_PriceDate",
                table: "AssetPrices",
                columns: new[] { "AssetId", "PriceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetPrices_AssetId_PriceDate_Source",
                table: "AssetPrices",
                columns: new[] { "AssetId", "PriceDate", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Cnpj",
                table: "Assets",
                column: "Cnpj");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Ticker",
                table: "Assets",
                column: "Ticker",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetValuationSnapshots_AssetId_Source_ReferenceDate",
                table: "AssetValuationSnapshots",
                columns: new[] { "AssetId", "Source", "ReferenceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceAllocations_AssetId",
                table: "ContributionGuidanceAllocations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Asse~",
                table: "ContributionGuidanceAllocations",
                columns: new[] { "ContributionGuidanceId", "AssetId" },
                unique: true,
                filter: "\"AssetId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceAllocations_ContributionGuidanceId_Cand~",
                table: "ContributionGuidanceAllocations",
                columns: new[] { "ContributionGuidanceId", "CandidateKey" },
                unique: true,
                filter: "\"CandidateKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidanceJobs_PortfolioId_ContributionGuidanceId~",
                table: "ContributionGuidanceJobs",
                columns: new[] { "PortfolioId", "ContributionGuidanceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionGuidances_PortfolioId_PortfolioSnapshotKey_Targ~",
                table: "ContributionGuidances",
                columns: new[] { "PortfolioId", "PortfolioSnapshotKey", "TargetAllocationKey", "IntelligenceContextKey", "RequestedAmount", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAnalyses_DocumentId_CreatedAt",
                table: "DocumentAnalyses",
                columns: new[] { "DocumentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAnalyses_DocumentId_Model_PromptVersion",
                table: "DocumentAnalyses",
                columns: new[] { "DocumentId", "Model", "PromptVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAnalyses_ProcessingJobId",
                table: "DocumentAnalyses",
                column: "ProcessingJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFinancialFacts_AssetId",
                table: "DocumentFinancialFacts",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFinancialFacts_DocumentId_AssetId_Metric_ReferenceP~",
                table: "DocumentFinancialFacts",
                columns: new[] { "DocumentId", "AssetId", "Metric", "ReferencePeriod", "PeriodType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_AssetId_PublishedAt",
                table: "Documents",
                columns: new[] { "AssetId", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Fingerprint",
                table: "Documents",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceName_SourceIdentifier",
                table: "Documents",
                columns: new[] { "SourceName", "SourceIdentifier" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceName_SourceUrl",
                table: "Documents",
                columns: new[] { "SourceName", "SourceUrl" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialHistoryCoverages_AssetId",
                table: "FinancialHistoryCoverages",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialHistoryCoverages_PortfolioId_AssetId_Dimension",
                table: "FinancialHistoryCoverages",
                columns: new[] { "PortfolioId", "AssetId", "Dimension" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialHistoryCoverages_PortfolioId_Dimension_FromDate_To~",
                table: "FinancialHistoryCoverages",
                columns: new[] { "PortfolioId", "Dimension", "FromDate", "ToDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEventAssets_InvestmentEventId",
                table: "InvestmentEventAssets",
                column: "InvestmentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEventEvidence_DocumentAnalysisId",
                table: "InvestmentEventEvidence",
                column: "DocumentAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEventEvidence_DocumentId",
                table: "InvestmentEventEvidence",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEventEvidence_InvestmentEventId",
                table: "InvestmentEventEvidence",
                column: "InvestmentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentEvents_DocumentAnalysisId_Fingerprint",
                table: "InvestmentEvents",
                columns: new[] { "DocumentAnalysisId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAssessments_InvestmentEventId",
                table: "PortfolioAssessments",
                column: "InvestmentEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAssessments_PortfolioId_InvestmentEventId_Portfoli~",
                table: "PortfolioAssessments",
                columns: new[] { "PortfolioId", "InvestmentEventId", "PortfolioSnapshotKey", "TargetAllocationKey", "AssessmentVersion" },
                unique: true,
                filter: "\"PortfolioSnapshotKey\" IS NOT NULL AND \"TargetAllocationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioCashAnchors_PortfolioId_AnchorDate",
                table: "PortfolioCashAnchors",
                columns: new[] { "PortfolioId", "AnchorDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioCashFlows_AssetId",
                table: "PortfolioCashFlows",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioCashFlows_PortfolioId_OccurredAt",
                table: "PortfolioCashFlows",
                columns: new[] { "PortfolioId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoricalAnchorItems_AnchorId_AssetId",
                table: "PortfolioHistoricalAnchorItems",
                columns: new[] { "AnchorId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoricalAnchorItems_AssetId",
                table: "PortfolioHistoricalAnchorItems",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoricalAnchors_PortfolioId_AnchorDate",
                table: "PortfolioHistoricalAnchors",
                columns: new[] { "PortfolioId", "AnchorDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioImportSessions_PortfolioId_Source_FileHash",
                table: "PortfolioImportSessions",
                columns: new[] { "PortfolioId", "Source", "FileHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioPositions_AssetId",
                table: "PortfolioPositions",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioPositions_PortfolioId_AssetId",
                table: "PortfolioPositions",
                columns: new[] { "PortfolioId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactionItems_AssetId",
                table: "PortfolioTransactionItems",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactionItems_PortfolioTransactionId",
                table: "PortfolioTransactionItems",
                column: "PortfolioTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_ContributionGuidanceId",
                table: "PortfolioTransactions",
                column: "ContributionGuidanceId",
                unique: true,
                filter: "\"ContributionGuidanceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_PortfolioId_ImportFingerprint",
                table: "PortfolioTransactions",
                columns: new[] { "PortfolioId", "ImportFingerprint" },
                unique: true,
                filter: "\"ImportFingerprint\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioTransactions_PortfolioId_OccurredAt",
                table: "PortfolioTransactions",
                columns: new[] { "PortfolioId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_DocumentId_JobType",
                table: "ProcessingJobs",
                columns: new[] { "DocumentId", "JobType" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_Status_AvailableAt",
                table: "ProcessingJobs",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RadarItemStates_DocumentAnalysisId",
                table: "RadarItemStates",
                column: "DocumentAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_RadarItemStates_PortfolioId_DocumentAnalysisId",
                table: "RadarItemStates",
                columns: new[] { "PortfolioId", "DocumentAnalysisId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TargetAllocations_PortfolioId_AssetClass",
                table: "TargetAllocations",
                columns: new[] { "PortfolioId", "AssetClass" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReconciliationMembers_PortfolioTransactionId",
                table: "TransactionReconciliationMembers",
                column: "PortfolioTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReconciliationMembers_ReconciliationId_Portfolio~",
                table: "TransactionReconciliationMembers",
                columns: new[] { "ReconciliationId", "PortfolioTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionReconciliations_PortfolioId_Status",
                table: "TransactionReconciliations",
                columns: new[] { "PortfolioId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetCorporateActions");

            migrationBuilder.DropTable(
                name: "AssetCurrencyExposures");

            migrationBuilder.DropTable(
                name: "AssetFundamentalSnapshots");

            migrationBuilder.DropTable(
                name: "AssetGeographicExposures");

            migrationBuilder.DropTable(
                name: "AssetPrices");

            migrationBuilder.DropTable(
                name: "AssetValuationSnapshots");

            migrationBuilder.DropTable(
                name: "ContributionGuidanceAllocations");

            migrationBuilder.DropTable(
                name: "ContributionGuidanceJobs");

            migrationBuilder.DropTable(
                name: "DocumentFinancialFacts");

            migrationBuilder.DropTable(
                name: "FinancialHistoryCoverages");

            migrationBuilder.DropTable(
                name: "InvestmentEventAssets");

            migrationBuilder.DropTable(
                name: "InvestmentEventEvidence");

            migrationBuilder.DropTable(
                name: "PortfolioAssessments");

            migrationBuilder.DropTable(
                name: "PortfolioCashAnchors");

            migrationBuilder.DropTable(
                name: "PortfolioCashFlows");

            migrationBuilder.DropTable(
                name: "PortfolioHistoricalAnchorItems");

            migrationBuilder.DropTable(
                name: "PortfolioImportSessions");

            migrationBuilder.DropTable(
                name: "PortfolioPositions");

            migrationBuilder.DropTable(
                name: "PortfolioTransactionItems");

            migrationBuilder.DropTable(
                name: "ProcessingJobs");

            migrationBuilder.DropTable(
                name: "RadarItemStates");

            migrationBuilder.DropTable(
                name: "TargetAllocations");

            migrationBuilder.DropTable(
                name: "TransactionReconciliationMembers");

            migrationBuilder.DropTable(
                name: "InvestmentEvents");

            migrationBuilder.DropTable(
                name: "PortfolioHistoricalAnchors");

            migrationBuilder.DropTable(
                name: "PortfolioTransactions");

            migrationBuilder.DropTable(
                name: "TransactionReconciliations");

            migrationBuilder.DropTable(
                name: "DocumentAnalyses");

            migrationBuilder.DropTable(
                name: "ContributionGuidances");

            migrationBuilder.DropTable(
                name: "Portfolios");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Assets");
        }
    }
}
