
using System;
using System.Collections.Generic;

namespace Armat.Threading;

// represents a Key, Value pair that can be set / get Job execution scope
public sealed class JobRuntimeScope : IDisposable
{
	private JobRuntimeScope(String key, Object value)
	{
		Key = key;
		Value = value;
	}
	public void Dispose()
	{
		Leave();
	}

	// enters a scope with a given key
	// Value will be initialized using the given factory() method
	// it will hold the Value while executing code within the scope
	// in case the scope key is already used, it will return the existing one
	public static JobRuntimeScope Enter(String key, Func<Object> factory)
	{
		return Create(key, factory);
	}
	// enters a scope with a given key
	// Value will be initialized using the given factory() method
	// it will hold the Value while executing code within the scope
	// in case the scope key is already used, it will return the existing one
	public static JobRuntimeScope Enter<T>(String key, Func<T> factory)
		where T : class
	{
		return Create(key, () => (Object)factory());
	}
	// enters a scope with a given T type
	// Value will be initialized using the given factory() method
	// it will hold the Value while executing code running within the scope
	// in case the scope key is already used, it will return the existing one
	public static JobRuntimeScope Enter<T>(Func<T> factory)
		where T : class
	{
		return Create(typeof(T).FullName!, () => factory());
	}

	private static JobRuntimeScope Create(String key, Func<Object> factory)
	{
		if (key.Length == 0)
			throw new ArgumentException("JobRuntimeScope key cannot be empty", nameof(key));

		// get or create thread runtime context to store the instance of JobRuntimeScope object
		ThreadRuntimeContext context = ThreadRuntimeContext.GetOrCreateCurrent();

		// check if it's already within the scope
		JobRuntimeScope result = context.GetScope(key);
		if (!result.IsNull)
		{
			// it should always create new scope, thus returning Null as a failure result
			// returning the existing scope would lead the following bug:
			// In case of a reentrant method, the same scope would be disposed when exiting the nested method
			return Null;
		}

		// create the scope
		result = new(key, factory())
		{
			_capturedContext = context
		};

		// register the scope
		if (!context.AddScope(result))
		{
			result.Key = Null.Key;
			result.Value = Null.Value;
			result._capturedContext = null;
		}

		return result;
	}

	// returns the scoped value corresponding to the given key within
	// the current JobRuntimeContext or ThreadRuntimeContext
	public static Object? GetValue(String key)
	{
		// lookup for the scope in the current job's runtime context
		JobRuntimeScope? scope = Job.Current?.RuntimeContext.GetScope(key);

		// if job runtime context is null, then
		// check if the scope is available in the thread runtime context
		scope ??= ThreadRuntimeContext.Current?.GetScope(key);

		if (scope != null && !scope.IsNull)
			return scope.Value;

		return null;
	}
	// returns the scoped value corresponding to the given key within
	// the current JobRuntimeContext or ThreadRuntimeContext
	public static T? GetValue<T>(String key)
		where T : class
	{
		Object? value = GetValue(key);
		if (value == null)
			return null;
			
		return (T)value;
	}
	// returns the scoped value corresponding to the given T type within
	// the current JobRuntimeContext or ThreadRuntimeContext
	public static T? GetValue<T>()
		where T : class
	{
		return GetValue(typeof(T).FullName!) as T;
	}

	public void Leave()
	{
		if (_capturedContext == null)
			return;

		_capturedContext.RemoveScope(Key);
		_capturedContext = null;
	}

	public static readonly JobRuntimeScope Null = new(String.Empty, new Object());
	public Boolean IsNull => Key.Length == 0;

	public String Key { get; private set; }
	public Object Value { get; private set; }

	private ThreadRuntimeContext? _capturedContext = null;
}

// JobRuntimeContext class holds collection of JobRuntimeScope objects within a given Job execution context
public struct JobRuntimeContext
{
	private Dictionary<String, JobRuntimeScope>? _currentScopes = null;

	public JobRuntimeContext()
	{
	}
	public static JobRuntimeContext Empty { get; } = new();

	public readonly Boolean IsEmpty
	{
		get { return _currentScopes == null || _currentScopes.Count == 0; }
	}
	public readonly JobRuntimeScope? GetScope(String key)
	{
		if (_currentScopes != null &&
			_currentScopes.TryGetValue(key, out JobRuntimeScope? scope) &&
			!scope.IsNull)
		{
			return scope;
		}

		return null;
	}

	// Sets Job Runtime Context to the one in the current thread
	// returns number of scopes in the context
	public Int32 Capture()
	{
		// merges with the current thread context
		ThreadRuntimeContext? currentThreadContext = ThreadRuntimeContext.Current;
		if (currentThreadContext == null)
			return 0;

		IReadOnlyCollection<JobRuntimeScope>? currentThreadScopes = currentThreadContext.Scopes;
		Int32 result = 0;

		if (currentThreadScopes != null && currentThreadScopes.Count > 0)
		{
			foreach (JobRuntimeScope scope in currentThreadScopes)
			{
				if (_currentScopes == null)
					_currentScopes = new Dictionary<String, JobRuntimeScope>();
				else if (_currentScopes.ContainsKey(scope.Key))
					continue;

				// clone the dictionary to add extra items
				if (result == 0 && _currentScopes.Count > 0)
					_currentScopes = new Dictionary<String, JobRuntimeScope>(_currentScopes);

				_currentScopes.Add(scope.Key, scope);
				result++;
			}
		}

		return result;
	}

	// merges given base runtime context with this one in the current thread
	// returns number of scopes in the context added on top of the base context
	public Int32 Capture(JobRuntimeContext baseContext)
	{
		// set the base context
		_currentScopes = baseContext._currentScopes;

		return Capture();
	}
}

// Thread runtime context keeps a map of key -> JobRuntimeScope objects
// There's a unique ThreadRuntimeContext instance per each thread
public class ThreadRuntimeContext
{
	private Dictionary<String, JobRuntimeScope>? _currentScopes = null;

	private ThreadRuntimeContext()
	{
	}

	public Boolean IsEmpty
	{
		get { return _currentScopes == null || _currentScopes.Count == 0; }
	}
	public IReadOnlyCollection<JobRuntimeScope>? Scopes
	{
		get { return _currentScopes?.Values; }
	}

	public Boolean Contains(String key)
	{
		return _currentScopes != null && _currentScopes.ContainsKey(key);
	}
	public JobRuntimeScope GetScope(String key)
	{
		if (_currentScopes != null && _currentScopes.TryGetValue(key, out JobRuntimeScope? scope))
			return scope;

		return JobRuntimeScope.Null;
	}

	public Boolean AddScope(JobRuntimeScope scope)
	{
		if (scope.IsNull)
			throw new ArgumentException("JobRuntimeScope.Enter failed", nameof(scope));

		_currentScopes ??= new Dictionary<String, JobRuntimeScope>();
		_currentScopes.Add(scope.Key, scope);

		JobMethodBuilderContext.AddScope(scope);

		return true;
	}
	public Boolean RemoveScope(String key)
	{
		if (_currentScopes == null || !_currentScopes.Remove(key))
			return false;

		JobMethodBuilderContext.RemoveScope(key);

		return true;
	}

	[ThreadStatic]
	private static ThreadRuntimeContext? _current;

	public static ThreadRuntimeContext? Current
	{
		get
		{
			return _current;
		}
		internal set
		{
			_current = value;
		}
	}
	public static ThreadRuntimeContext GetOrCreateCurrent()
	{
		_current ??= new ThreadRuntimeContext();

		return _current;
	}
}

// created with each JobMethodBuilder to collect and reset list of scopes 
// within the context of async method execution
public struct JobMethodBuilderContext : IDisposable
{
	[ThreadStatic]
	private static Stack<JobMethodBuilderContext>? _jmbStack;

	private List<JobRuntimeScope>? _listScopes;

	public JobMethodBuilderContext()
	{
		_listScopes = null;

		_jmbStack ??= new Stack<JobMethodBuilderContext>();
		_jmbStack.Push(this);
	}

	public static Boolean AddScope(JobRuntimeScope scope)
	{
		if (_jmbStack == null || _jmbStack.Count == 0)
			return false;

		Boolean result;
		JobMethodBuilderContext cxt = _jmbStack.Peek();
		if (cxt._listScopes == null)
		{
			// data member _listScopes of a structure is being changed
			cxt = _jmbStack.Pop();
			result = cxt.AddImpl(scope);
			_jmbStack.Push(cxt);
		}
		else
		{
			// add element to the list
			result = cxt.AddImpl(scope);
		}

		return result;
	}
	public static Boolean RemoveScope(String key)
	{
		if (_jmbStack == null || _jmbStack.Count == 0)
			return false;

		JobMethodBuilderContext cxt = _jmbStack.Peek();
		Boolean result = cxt.RemoveImpl(key);

		return result;
	}

	private Boolean AddImpl(JobRuntimeScope scope)
	{
		if (scope == null || scope.IsNull)
			return false;

		_listScopes ??= new List<JobRuntimeScope>();
		_listScopes.Add(scope);

		return true;
	}
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0251:Make member 'readonly'", Justification = "<Pending>")]
	private Boolean RemoveImpl(String key)
	{
		if (_listScopes == null)
			return false;

		// most probably the last one added should be removed first
		for (Int32 i = _listScopes.Count - 1; i >= 0; i--)
		{
			if (_listScopes[i].Key == key)
			{
				// remove the one matching by key
				_listScopes.RemoveAt(i);

				return true;
			}
		}

		return false;
	}
	private void ResetImpl()
	{
		if (_listScopes == null || _listScopes.Count == 0)
			return;

		// get current thread runtime context
		ThreadRuntimeContext? currentThreadRuntimeContext = ThreadRuntimeContext.Current;
		if (currentThreadRuntimeContext == null)
		{
			System.Diagnostics.Debug.Assert(false, "ThreadRuntimeContext.Current cannot be null while there are scopes registered in JobMethodBuilderContext");
			return;
		}

		// reset the scopes variable to prevent re-entrance of JobMethodBuilderContext.Leave method
		List<JobRuntimeScope> scopes = _listScopes;
		_listScopes = null;

		// dispose all scoped when coming out of method builder context
		for (Int32 i = scopes.Count - 1; i >= 0; i--)
			scopes[i].Dispose();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0251:Make member 'readonly'", Justification = "<Pending>")]
	public void Dispose()
	{
		if (_jmbStack == null || _jmbStack.Count == 0)
		{
			System.Diagnostics.Debug.Assert(false, "JobMethodBuilderContext creation and disposal operations must run in LIFO order");
			return;
		}

		JobMethodBuilderContext cxt = _jmbStack.Pop();

		// this will dispose all scopes within this JobMethodBuilderContext
		// note: this object has null scopes (because it's a struct created on a stack), but the cxt read from the _listScopes has all of those
		cxt.ResetImpl();
	}
}
