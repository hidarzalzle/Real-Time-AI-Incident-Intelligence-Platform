namespace Infrastructure;

public sealed class RabbitOptions
{
    public const string Section = "Rabbit";
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public int RetryCount { get; set; } = 3;
}

public sealed class RedisOptions
{
    public const string Section = "Redis";
    public string ConnectionString { get; set; } = "redis:6379";
}

public sealed class ElasticOptions
{
    public const string Section = "Elastic";
    public string Endpoint { get; set; } = "http://elasticsearch:9200";
}

public sealed class ProcessingOptions
{
    public const string Section = "Processing";
    public int AutoResolveQuietMinutes { get; set; } = 5;
}
