using Azure.Communication;
using Azure.Communication.CallAutomation;
using Azure.Messaging;
using PlayAudio.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(new CallAutomationClient(builder.Configuration["ACS:ConnectionString"]));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/api/calls", async (CallRequest request, CallAutomationClient client) =>
{
    var applicationId = new CommunicationUserIdentifier(builder.Configuration["ACS:ApplicationId"]);
    var callSource = new CallSource(applicationId)
    {
        CallerId = new PhoneNumberIdentifier(request.Source),
        DisplayName = request.DisplayName
    };

    var targets = new List<CommunicationIdentifier>()
    {
        new PhoneNumberIdentifier(request.Destination)
    };

    var createCallOptions = new CreateCallOptions(callSource, targets,
        new Uri(builder.Configuration["VS_TUNNEL_URL"] + "api/callbacks"));

    await client.CreateCallAsync(createCallOptions);
});

app.MapPost("/api/callbacks", async (CloudEvent[] events, CallAutomationClient client, ILogger<Program> logger) =>
{
    foreach (var cloudEvent in events)
    {
        var @event = CallAutomationEventParser.Parse(cloudEvent);
        logger.LogInformation($"Received {@event.GetType()}");
        
        if (@event is CallConnected callConnected)
        {
            logger.LogInformation($"Call connected: {callConnected.CallConnectionId} | {callConnected.CorrelationId}");
            var playSource = new FileSource(new Uri(builder.Configuration["ACS:AudioFile"]))
            {
                PlaySourceId = ""
            };

            var playOptions = new PlayOptions()
            {
                OperationContext = "WelcomeMessage"
            };

            await client.GetCallConnection(callConnected.CallConnectionId).GetCallMedia().PlayToAllAsync(playSource, playOptions);
        }

        if (@event is PlayCompleted playCompleted)
        {
            logger.LogInformation($"Play completed! The OperationContext is {playCompleted.OperationContext}.");
        }

        if (@event is PlayFailed playFailed)
        {
            logger.LogError($"Play failed: {playFailed.ResultInformation.Message}");
        }
    }
});

app.Run();