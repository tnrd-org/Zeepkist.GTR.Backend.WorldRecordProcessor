using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TNRD.Zeepkist.GTR.Backend.WorldRecordProcessor.Rabbit;
using TNRD.Zeepkist.GTR.Database;
using TNRD.Zeepkist.GTR.Database.Models;
using TNRD.Zeepkist.GTR.DTOs.Rabbit;

namespace TNRD.Zeepkist.GTR.Backend.WorldRecordProcessor;

internal class QueueProcessor : IHostedService
{
    private readonly ItemQueue itemQueue;
    private readonly IServiceProvider serviceProvider;
    private readonly IRabbitPublisher publisher;
    private readonly ILogger<QueueProcessor> logger;

    private readonly CancellationTokenSource cts;

    private Task? queueRunnerTask;

    public QueueProcessor(
        ItemQueue itemQueue,
        IServiceProvider serviceProvider,
        ILogger<QueueProcessor> logger,
        IRabbitPublisher publisher
    )
    {
        this.itemQueue = itemQueue;
        this.serviceProvider = serviceProvider;
        this.logger = logger;
        this.publisher = publisher;

        cts = new CancellationTokenSource();
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        queueRunnerTask = QueueRunner(cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cts.Cancel();

        if (queueRunnerTask != null)
        {
            await queueRunnerTask;
        }

        cts.Dispose();
    }

    private async Task? QueueRunner(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            List<KeyValuePair<int, List<ProcessWorldRecordRequest>>[]> chunks = itemQueue.GetItemsFromQueue()
                .Chunk(10).ToList();

            foreach (KeyValuePair<int, List<ProcessWorldRecordRequest>>[] chunk in chunks)
            {
                List<IServiceScope> scopes = new();
                List<Task> tasks = new();

                foreach (KeyValuePair<int, List<ProcessWorldRecordRequest>> kvp in chunk)
                {
                    IServiceScope scope = serviceProvider.CreateScope();
                    scopes.Add(scope);
                    Task task = ProcessQueue(scope.ServiceProvider,
                        kvp.Key,
                        kvp.Value,
                        CancellationToken.None); // TODO: Check if we should give a different CT here
                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);

                foreach (IServiceScope scope in scopes)
                {
                    scope.Dispose();
                }
            }

            if (!itemQueue.HasItems())
                await Task.Delay(1000, ct);
        }
    }

    private async Task ProcessQueue(
        IServiceProvider provider,
        int level,
        List<ProcessWorldRecordRequest> requests,
        CancellationToken ct
    )
    {
        GTRContext context = provider.GetRequiredService<GTRContext>();
        using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(ct))
        {
            try
            {
                List<Record> worldRecords = await context.Records
                    .Where(x => x.Level == level && x.IsWr)
                    .ToListAsync(ct);

                foreach (Record worldRecord in worldRecords)
                {
                    worldRecord.IsWr = false;
                }

                Record? record = await context.Records
                    .Where(x => x.Level == level && x.IsValid)
                    .OrderBy(x => x.Time)
                    .FirstOrDefaultAsync(ct);

                if (record == null)
                {
                    logger.LogError("Unable to mark record as WR because it does not exist");
                }
                else
                {
                    record.IsWr = true;
                }

                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                foreach (Record worldRecord in worldRecords)
                {
                    publisher.Publish("records",
                        new RecordId
                        {
                            Id = worldRecord.Id
                        });
                }

                if (record != null)
                {
                    publisher.Publish("records",
                        new RecordId
                        {
                            Id = record.Id
                        });
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Unable to process queue");
                await transaction.RollbackAsync(ct);
            }
        }
    }
}
