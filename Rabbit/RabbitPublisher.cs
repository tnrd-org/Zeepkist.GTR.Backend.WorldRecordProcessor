using System.Text;
using Newtonsoft.Json;
using RabbitMQ.Client;

namespace TNRD.Zeepkist.GTR.Backend.WorldRecordProcessor.Rabbit;

internal class RabbitPublisher : IRabbitPublisher
{
    private readonly ILogger<RabbitPublisher> logger;

    private IModel? channel;

    public RabbitPublisher(ILogger<RabbitPublisher> logger)
    {
        this.logger = logger;
    }

    public void Initialize(IModel channel)
    {
        logger.LogInformation("Initializing RabbitMQ Publisher");
        this.channel = channel;
    }

    public void Publish(string exchange, object data)
    {
        if (channel == null)
        {
            logger.LogWarning("Cannot publish to RabbitMQ, channel is not initialized");
            return;
        }

        try
        {
            string json = JsonConvert.SerializeObject(data);
            byte[] body = Encoding.UTF8.GetBytes(json);
            channel.BasicPublish(exchange, string.Empty, null, body);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to publish to RabbitMQ");
        }
    }
}
