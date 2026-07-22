using GalleryDl.WebApi.Options;
using GalleryDl.WebApi.Services;
using Microsoft.AspNetCore.StaticFiles;

namespace GalleryDl.WebApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            builder.Services.AddOpenApi();

            builder.Services.AddOptions<GalleryDlOptions>()
                .BindConfiguration(GalleryDlOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            builder.Services.AddSingleton<GalleryDlRunner>();
            builder.Services.AddSingleton<FileExtensionContentTypeProvider>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.MapControllers();

            app.Run();
        }
    }
}
