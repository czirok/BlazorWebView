using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Web;
using WebKit;
using Uri = System.Uri;

namespace WebKitGtk;

[UnsupportedOSPlatform("OSX")]
[UnsupportedOSPlatform("Windows")]
public class BlazorWebView : WebView
{
	public BlazorWebView(IServiceProvider serviceProvider)
	{
		_ = new WebViewManager(this, serviceProvider);
	}
}

[UnsupportedOSPlatform("OSX")]
[UnsupportedOSPlatform("Windows")]
internal class WebViewManager : Microsoft.AspNetCore.Components.WebView.WebViewManager, IAsyncDisposable
{
	private const string Scheme = "app";
	private static readonly Uri BaseUri = new($"{Scheme}://localhost/");
	private readonly UserContentManager? _userContentManager;
	private readonly WebView _webView;
	private readonly BlazorWebViewOptions options;

	public WebViewManager(WebView webView, IServiceProvider serviceProvider) : base(
		serviceProvider,
		new GtkDispatcher(serviceProvider.GetRequiredService<IDispatcher>()),
		BaseUri,
		new PhysicalFileProvider(serviceProvider.GetRequiredService<BlazorWebViewOptions>().ContentRoot),
		new(),
		serviceProvider.GetRequiredService<BlazorWebViewOptions>().RelativeHostPath)
	{
		options = serviceProvider.GetRequiredService<BlazorWebViewOptions>();

		_webView = webView;

		// This is necessary to automatically serve the files in the `_framework` virtual folder.
		// Using `file://` will cause the webview to look for the `_framework` files on the file system,
		// and it won't find them.
		if (_webView.WebContext is null)
		{
			throw new Exception("WebView.WebContext is null");
		}
		_webView.OnCreate += NavigationSignalHandler;

		try
		{
			_webView.WebContext.RegisterUriScheme(Scheme, HandleUriScheme);
		}
		catch (Exception ex)
		{
			throw new Exception($"Failed to register URI scheme: {Scheme}", ex);
		}

		Dispatcher.InvokeAsync(async () =>
		{
			await AddRootComponentAsync(options.RootComponent, "#app", Microsoft.AspNetCore.Components.ParameterView.Empty);
		});

		_userContentManager = webView.GetUserContentManager();
		_userContentManager.AddScript(UserScript.New(
			source:
			"""
				window.__receiveMessageCallbacks = [];

				window.__dispatchMessageCallback = function(message) {
					window.__receiveMessageCallbacks.forEach(function(callback) { callback(message); });
				};

				window.external = {
					sendMessage: function(message) {
						window.webkit.messageHandlers.webview.postMessage(message);
					},
					receiveMessage: function(callback) {
						window.__receiveMessageCallbacks.push(callback);
					}
				};
			""",
			injectedFrames: UserContentInjectedFrames.AllFrames,
			injectionTime: UserScriptInjectionTime.Start,
			null,
			null)
		);

		UserContentManager
			.ScriptMessageReceivedSignal
				.Connect(_userContentManager, WebviewInteropMessageReceived, true, "webview");

		if (!_userContentManager.RegisterScriptMessageHandler("webview", null))
		{
			throw new Exception("Could not register script message handler");
		}

		Navigate("/");
	}

	private void HandleUriScheme(URISchemeRequest request)
	{
		if (request.GetScheme() != Scheme)
		{
			throw new Exception($"Invalid scheme \"{request.GetScheme()}\"");
		}

		var uri = request.GetUri();
		if (request.GetPath() == "/")
		{
			uri += options.RelativeHostPath;
		}

		if (TryGetResponseContent(uri, false, out var statusCode, out var statusMessage, out var content, out var headers))
		{
			using var memoryStream = new MemoryStream();
			content.CopyTo(memoryStream);
			var inputStream = Gio.MemoryInputStream.NewFromBytes(GLib.Bytes.New(memoryStream.ToArray()));
			request.Finish(inputStream, memoryStream.Length, headers["Content-Type"]);
		}
		else
		{
			throw new Exception($"Failed to serve \"{uri}\". {statusCode} - {statusMessage}");
		}
	}

	protected override void NavigateCore(Uri absoluteUri)
	{
		_webView.LoadUri(absoluteUri.ToString());
	}

	protected override async void SendMessage(string message)
	{
		var script = $"__dispatchMessageCallback(\"{HttpUtility.JavaScriptStringEncode(message)}\")";
		_ = await _webView.EvaluateJavascriptAsync(script);
	}

	private void WebviewInteropMessageReceived(UserContentManager sender, UserContentManager.ScriptMessageReceivedSignalArgs args)
	{
		var result = args.Value;
		MessageReceived(BaseUri, result.ToString());
	}

	private Widget NavigationSignalHandler(WebView sender, WebView.CreateSignalArgs args)
	{
		var navigationType = WebKit.Internal.NavigationAction.GetNavigationType(args.NavigationAction.Handle);
		if (navigationType != NavigationType.LinkClicked) return default!;

		var request = WebKit.Internal.NavigationAction.GetRequest(args.NavigationAction.Handle);
		var nonNullableUtf8StringUnownedHandle = WebKit.Internal.URIRequest.GetUri(request);
		var uri = nonNullableUtf8StringUnownedHandle.ConvertToString();
		LaunchUriInExternalBrowser(uri);
		return default!;
	}

	private void LaunchUriInExternalBrowser(string webviewUri)
	{
		if (Uri.TryCreate(webviewUri, UriKind.Absolute, out var uri))
		{
			using var launchBrowser = new Process();
			launchBrowser.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
			launchBrowser.StartInfo.UseShellExecute = true;
			launchBrowser.StartInfo.FileName = uri.ToString();
			launchBrowser.Start();
		}
	}

	protected override async ValueTask DisposeAsyncCore()
	{
		await base.DisposeAsyncCore();
		_webView.OnCreate -= NavigationSignalHandler;
		if (_userContentManager is null) return;
		UserContentManager.ScriptMessageReceivedSignal.Disconnect(_userContentManager, WebviewInteropMessageReceived);
		_userContentManager.Dispose();
	}
}
