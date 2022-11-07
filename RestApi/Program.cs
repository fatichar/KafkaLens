using KafkaLens.Core.DataAccess;
using KafkaLens.Core.Services;
using KafkaLens.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1",
        new OpenApiInfo
        {
            Title = "KafkaLens Api",
            Version = "v1"
        });
    // c.DocumentFilter<BasePathFilter>();
    // use action name as operation id
    c.CustomOperationIds(e => $"{e.ActionDescriptor.RouteValues["action"]}");
});

builder.Services.AddDbContext<KlServerContext>(opt => opt.UseSqlite("Data Source=KafkaDB.db;"));

// TODO replace with SharedClusterService
builder.Services.AddSingleton<IKafkaLensClient, SharedClient>();
builder.Services.AddSingleton<ConsumerFactory>();
builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

var app = builder.Build();

app.UseCors(x => x
    .AllowAnyMethod()
    .AllowAnyHeader()
    .SetIsOriginAllowed(origin => true) // allow any origin  
    .AllowCredentials());               // allow credentials 

app.UseSwagger();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "KafkaLens Api");
    });
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


app.UseHttpsRedirection();

//app.UseBlazorFrameworkFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();