using SaigonWaterbus.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    await app.InitialiseDatabaseAsync();
}
else
{ 
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("FrontendClientPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.UseFileServer();

app.MapOpenApi();
app.UseSwagger();
app.UseSwaggerUI();

app.Map("/", () => Results.Redirect("/swagger"));

app.MapEndpoints(typeof(Program).Assembly);


app.Run();

public partial class Program;
