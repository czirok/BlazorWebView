using Microsoft.Extensions.DependencyInjection;

namespace WebKitGtk;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddBlazorWebView(this IServiceCollection services, BlazorWebViewOptions options)
	{
		WebKit.Module.Initialize();

		return services
			.AddDispatcher()
			.AddBlazorWebView()
			.AddSingleton(options);
	}

	public static IServiceCollection AddDispatcher(this IServiceCollection services)
	{
		services.AddScoped<IDispatcher>(_ =>
			new Dispatcher(SynchronizationContext.Current!));

		services.AddScoped<Microsoft.AspNetCore.Components.Dispatcher>(sp =>
			new GtkDispatcher(sp.GetRequiredService<IDispatcher>()));
		return services;
	}
}