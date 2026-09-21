using Prumo.Domain.Entities;
using Prumo.Domain.Enums;

namespace Prumo.Domain.Tests;

public sealed class ProcessingJobTests
{
    [Fact]
    public void Espera_por_conteudo_em_processamento_nao_consume_tentativa()
    {
        var job = CreateJob();
        var now = DateTimeOffset.UtcNow;
        job.Start(now);
        job.Fail(now, "timeout anterior", 3, TimeSpan.Zero);
        job.Start(now);
        job.Defer(now.AddMinutes(1));

        Assert.Equal(ProcessingJobStatus.Queued, job.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Null(job.StartedAt);
        Assert.Equal(now.AddMinutes(1), job.AvailableAt);
        job.Start(now.AddMinutes(1));
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public void Job_inicia_enfileirado_com_zero_tentativas()
    {
        var job = CreateJob();

        Assert.Equal(ProcessingJobStatus.Queued, job.Status);
        Assert.Equal(0, job.Attempts);
    }

    [Fact]
    public void Falha_reenfileira_com_backoff_ate_o_limite()
    {
        var job = CreateJob();
        var now = DateTimeOffset.UtcNow;

        job.Start(now);
        var retry = job.Fail(now, "timeout", 3, TimeSpan.FromMinutes(1));

        Assert.True(retry);
        Assert.Equal(ProcessingJobStatus.Queued, job.Status);
        Assert.Equal(now.AddMinutes(1), job.AvailableAt);
        Assert.Equal("timeout", job.LastError);
    }

    [Fact]
    public void Falha_na_terceira_tentativa_termina_em_erro()
    {
        var job = CreateJob();
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            job.Start(now.AddMinutes(attempt));
            var retry = job.Fail(now.AddMinutes(attempt), "falha permanente", 3, TimeSpan.Zero);
            if (attempt == 2) Assert.False(retry);
        }

        Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        Assert.Equal(3, job.Attempts);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public void Conclusao_registra_data_e_impede_nova_execucao()
    {
        var job = CreateJob();
        var now = DateTimeOffset.UtcNow;
        job.Start(now);
        job.Complete(now.AddSeconds(2));

        Assert.Equal(ProcessingJobStatus.Completed, job.Status);
        Assert.Equal(now.AddSeconds(2), job.CompletedAt);
        Assert.Throws<InvalidOperationException>(() => job.Start(now.AddSeconds(3)));
    }

    private static ProcessingJob CreateJob() => new(Guid.NewGuid(), ProcessingJobType.DownloadDocument, DateTimeOffset.UtcNow);
}
