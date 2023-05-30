using Serilog;
using TNRD.Zeepkist.GTR.Backend.WorldRecordProcessor.Rabbit;
using TNRD.Zeepkist.GTR.Database;

namespace TNRD.Zeepkist.GTR.Backend.WorldRecordProcessor;

internal class Program
{
    public static void Main(string[] args)
    {
        IHost host = Host.CreateDefaultBuilder(args)
            .UseSerilog((context, configuration) =>
            {
                configuration
                    .MinimumLevel.Information()
                    .WriteTo.Console();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddHostedService<QueueProcessor>();

                services.Configure<RabbitOptions>(context.Configuration.GetSection("Rabbit"));
                services.AddSingleton<IRabbitPublisher, RabbitPublisher>();
                services.AddHostedService<RabbitWorker>();

                services.AddNpgsql<GTRContext>(context.Configuration["Database:ConnectionString"]);

                services.AddSingleton<ItemQueue>();
            })
            .Build();

        ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();

        TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
        {
            logger.LogError(eventArgs.Exception, "Unobserved task exception");
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            logger.LogError(eventArgs.ExceptionObject as Exception, "Unhandled exception");
        };

        host.Run();
    }
}
