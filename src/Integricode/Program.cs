using Integricode;
using Integricode.Agent;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var httpHandler = new SocketsHttpHandler();
return await new App(httpHandler, ChatClientFactory.Create)
    .RunAsync(args, Environment.GetEnvironmentVariable, Console.Out, Console.Error, cancellation.Token);
