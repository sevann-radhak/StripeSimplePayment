using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Stripe;

DotNetEnv.Env.Load();

StripeConfiguration.AppInfo = new AppInfo
{
    Name = "stripe-samples/accept-a-payment/custom-payment-flow",
    Url = "https://github.com/stripe-samples",
    Version = "0.1.0",
};

StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<StripeOptions>(options =>
{
    options.PublishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY");
    options.SecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
    options.WebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
});

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    _ = app.UseDeveloperExceptionPage();
}

app.UseStaticFiles(new StaticFileOptions()
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(),
        "../../client/html")
    ),
    RequestPath = new PathString("")
});

app.MapGet("/", () => Results.Redirect("index.html"));
app.MapGet("/config", (IOptions<StripeOptions> options) => new { options.Value.PublishableKey });

app.MapPost("/create-payment-intent", async (CreatePaymentIntentRequest req) =>
{
    List<string> formattedPaymentMethodType = req.PaymentMethodType == "link" ? new List<string> { "link", "card" } : new List<string> { req.PaymentMethodType };
    PaymentIntentCreateOptions options = new()
    {
        Amount = 5999,
        Currency = req.Currency,
        PaymentMethodTypes = formattedPaymentMethodType
    };

    // If this is for an ACSS payment, we add payment_method_options to create
    // the Mandate.
    if (req.PaymentMethodType == "acss_debit")
    {
        options.PaymentMethodOptions = new PaymentIntentPaymentMethodOptionsOptions
        {
            AcssDebit = new PaymentIntentPaymentMethodOptionsAcssDebitOptions
            {
                MandateOptions = new PaymentIntentPaymentMethodOptionsAcssDebitMandateOptionsOptions
                {
                    PaymentSchedule = "sporadic",
                    TransactionType = "personal",
                },
            }
        };
    }

    PaymentIntentService service = new();

    try
    {
        PaymentIntent paymentIntent = await service.CreateAsync(options);

        return Results.Ok(new
        {
            paymentIntent.ClientSecret,
        });
    }
    catch (StripeException e)
    {
        return Results.BadRequest(new { error = new { message = e.StripeError.Message } });
    }
});

app.MapGet("/payment/next", (HttpRequest request, HttpResponse response) =>
{
    Microsoft.Extensions.Primitives.StringValues paymentIntent = request.Query["payment_intent"];
    PaymentIntentService service = new();
    PaymentIntent intent = service.Get(paymentIntent);

    response.Redirect("/success?payment_intent_client_secret={intent.ClientSecret}");
});

app.MapGet("/success", () => Results.Redirect("success.html"));

app.MapPost("/webhook", async (HttpRequest request, IOptions<StripeOptions> options) =>
{
    string json = await new StreamReader(request.Body).ReadToEndAsync();
    Event stripeEvent;
    try
    {
        stripeEvent = EventUtility.ConstructEvent(
            json,
            request.Headers["Stripe-Signature"],
            options.Value.WebhookSecret
        );
        app.Logger.LogInformation($"Webhook notification with type: {stripeEvent.Type} found for {stripeEvent.Id}");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Something failed");
        return Results.BadRequest();
    }

    if (stripeEvent.Type == "payment_intent.succeeded")
    {
        PaymentIntent paymentIntent = stripeEvent.Data.Object as Stripe.PaymentIntent;
        app.Logger.LogInformation($"PaymentIntent ID: {paymentIntent.Id}");
        // Take some action based on session.
    }

    return Results.Ok();
});

app.Run();

