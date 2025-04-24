namespace WebKitGtk;

/// <summary>
/// This class is a wrapper around the IDispatcher interface to provide
/// compatibility with the Microsoft.AspNetCore.Components.Dispatcher.
/// It allows the Blazor WebView to use the GLib.Internal.MainLoopSynchronizationContext.
/// This is necessary because the Blazor WebView uses a different
/// synchronization context than the one used by the Blazor framework.
/// </summary>
/// <param name="dispatcher"></param>
internal sealed class GtkDispatcher(IDispatcher dispatcher) : Microsoft.AspNetCore.Components.Dispatcher
{
	public override bool CheckAccess() => !dispatcher.IsDispatchRequired;
	public override Task InvokeAsync(Action workItem) => dispatcher.DispatchAsync(workItem);
	public override Task InvokeAsync(Func<Task> workItem) => dispatcher.DispatchAsync(workItem);
	public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) => dispatcher.DispatchAsync(workItem);
	public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem) => dispatcher.DispatchAsync(workItem);
}

internal interface IDispatcher
{
	bool Dispatch(Action action);
	bool IsDispatchRequired { get; }
}

internal sealed class Dispatcher(SynchronizationContext context) : IDispatcher
{
	public bool IsDispatchRequired => context != SynchronizationContext.Current;

	public bool Dispatch(Action action)
	{
		context.Post(_ => action(), null);
		return true;
	}
}

internal static class DispatcherExtensions
{
	public static Task DispatchAsync(this IDispatcher dispatcher, Action action) =>
		dispatcher.DispatchAsync(() => { action(); return true; });

	public static Task<T> DispatchAsync<T>(this IDispatcher dispatcher, Func<T> func)
	{
		var tcs = new TaskCompletionSource<T>();
		dispatcher.Dispatch(() =>
		{
			try { tcs.SetResult(func()); }
			catch (Exception e) { tcs.SetException(e); }
		});
		return tcs.Task;
	}

	public static Task<T> DispatchAsync<T>(this IDispatcher dispatcher, Func<Task<T>> funcTask)
	{
		var tcs = new TaskCompletionSource<T>();
		dispatcher.Dispatch(async () =>
		{
			try { tcs.SetResult(await funcTask().ConfigureAwait(false)); }
			catch (Exception e) { tcs.SetException(e); }
		});
		return tcs.Task;
	}

	public static Task DispatchAsync(this IDispatcher dispatcher, Func<Task> funcTask) =>
		dispatcher.DispatchAsync(async () => { await funcTask().ConfigureAwait(false); return true; });
}
