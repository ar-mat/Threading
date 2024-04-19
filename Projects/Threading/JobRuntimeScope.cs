
using System;
using System.Collections.Generic;

namespace Armat.Threading
{
	public sealed class JobRuntimeScope : IDisposable
	{
		public static JobRuntimeScope Enter(String key, Func<Object> factory)
		{
			if (String.IsNullOrEmpty(key))
				throw new ArgumentException("JobRuntimeScope key cannot be empty", nameof(key));
			if (factory == null)
				throw new ArgumentNullException(nameof(factory));

			// check if it's already within the scope
			ThreadRuntimeContext context = ThreadRuntimeContext.GetOrCreateCurrent();
			if (context.Contains(key))
				return Null;

			// create the scope
			JobRuntimeScope scope = new()
			{
				Key = key,
				Factory = factory,
				_capturedContext = context
			};

			// register the scope
			if (!context.Enter(scope))
				scope._capturedContext = null;

			return scope;
		}
		public static JobRuntimeScope Enter<T>(Func<T> factory)
		{
			if (factory == null)
				throw new ArgumentNullException(nameof(factory));

			return Enter(typeof(T).FullName, () => factory());
		}

		public static T GetObject<T>() where T : class
		{
			return GetObject(typeof(T).FullName) as T;
		}
		public static T GetObject<T>(String key) where T : class
		{
			return GetObject(key) as T;
		}
		public static Object GetObject(String key)
		{
			// lookup for the object in the current job's scope
			Object result = Job.Current?.RuntimeContext.GetScope(key)?.GetObject();
			if (result == null)
			{
				// check if the object is set in the thread runtime context
				result = ThreadRuntimeContext.Current?.GetScope(key)?.GetObject();
			}

			return result;
		}

		private JobRuntimeScope()
		{
		}
		public void Dispose()
		{
			if (_capturedContext == null)
				return;

			_capturedContext.Leave(Key);
			_capturedContext = null;
		}

		public static readonly JobRuntimeScope Null = new();
		public Boolean IsNull => _capturedContext == null;

		public String Key { get; private set; }
		public Func<Object> Factory { get; private set; }

		private ThreadRuntimeContext _capturedContext = null;

		private Object _runtimeObject = null;
		public Object GetObject()
		{
			// try to get
			if (_runtimeObject != null)
				return _runtimeObject;

			// create
			lock (this)
			{
				if (_runtimeObject == null)
					_runtimeObject = Factory();
			}

			return _runtimeObject;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "<Pending>")]
	public struct JobRuntimeContext
	{
		private Dictionary<String, JobRuntimeScope> _currentScopes;

		public JobRuntimeContext()
		{
			_currentScopes = new Dictionary<String, JobRuntimeScope>();
		}
		public static JobRuntimeContext Empty { get; } = new();

		public Boolean IsEmpty
		{
			get { return _currentScopes == null || _currentScopes.Count == 0; }
		}
		public JobRuntimeScope GetScope(String key)
		{
			if (_currentScopes != null && _currentScopes.TryGetValue(key, out JobRuntimeScope scope))
				return scope;

			return null;
		}

		// Sets Job Runtime Context to the one in the current thread
		// returns number of scopes in the context
		public Int32 Capture()
		{
			// merger with the current thread context
			ThreadRuntimeContext currentThreadContext = ThreadRuntimeContext.Current;
			if (currentThreadContext == null)
				return 0;

			IReadOnlyCollection<JobRuntimeScope> currentThreadScopes = currentThreadContext.Scopes;
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

	public class ThreadRuntimeContext
	{
		private Dictionary<String, JobRuntimeScope> _currentScopes;

		private ThreadRuntimeContext()
		{
			_currentScopes = new Dictionary<String, JobRuntimeScope>();
		}

		public Boolean IsEmpty
		{
			get { return _currentScopes == null || _currentScopes.Count == 0; }
		}
		public IReadOnlyCollection<JobRuntimeScope> Scopes
		{
			get { return _currentScopes?.Values; }
		}

		public Boolean Contains(String key)
		{
			return _currentScopes != null && _currentScopes.ContainsKey(key);
		}
		public JobRuntimeScope GetScope(String key)
		{
			if (_currentScopes != null && _currentScopes.TryGetValue(key, out JobRuntimeScope scope))
				return scope;

			return null;
		}

		public Boolean Enter(JobRuntimeScope scope)
		{
			if (scope == null || scope.IsNull)
				throw new ArgumentException("JobRuntimeScope.Enter failed", nameof(scope));

			if (_currentScopes == null)
				_currentScopes = new Dictionary<String, JobRuntimeScope>();

			_currentScopes.Add(scope.Key, scope);
			JobMethodBuilderContext.Enter(scope);

			return true;
		}
		public Boolean Leave(String key)
		{
			if (_currentScopes == null)
				return false;

			if (!_currentScopes.Remove(key))
				return false;

			JobMethodBuilderContext.Leave(key);
			return true;
		}

		[ThreadStatic]
		private static ThreadRuntimeContext _current;

		public static ThreadRuntimeContext Current
		{
			get
			{
				return _current;
			}
		}
		public static ThreadRuntimeContext GetOrCreateCurrent()
		{
			if (_current == null)
				_current = new ThreadRuntimeContext();

			return _current;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "<Pending>")]
	public struct JobMethodBuilderContext : IDisposable
	{
		[ThreadStatic]
		private static Stack<JobMethodBuilderContext> _jmbStack;

		private List<JobRuntimeScope> _listScopes;

		public JobMethodBuilderContext()
		{
			_listScopes = null;

			if (_jmbStack == null)
				_jmbStack = new Stack<JobMethodBuilderContext>();
			_jmbStack.Push(this);
		}

		public static Boolean Enter(JobRuntimeScope scope)
		{
			if (_jmbStack == null || _jmbStack.Count == 0)
				return false;

			Boolean result;
			JobMethodBuilderContext cxt = _jmbStack.Peek();
			if (cxt._listScopes == null)
			{
				// the structure member _listScopes is being changed
				cxt = _jmbStack.Pop();
				result = cxt.EnterImpl(scope);
				_jmbStack.Push(cxt);
			}
			else
			{
				result = cxt.EnterImpl(scope);
			}

			return result;
		}
		public static Boolean Leave(String key)
		{
			if (_jmbStack == null || _jmbStack.Count == 0)
				return false;

			JobMethodBuilderContext cxt = _jmbStack.Peek();
			Boolean result = cxt.LeaveImpl(key);

			return result;
		}

		private Boolean EnterImpl(JobRuntimeScope scope)
		{
			if (scope == null || scope.IsNull)
				throw new ArgumentException("JobMethodBuilderContext.Enter failed", nameof(scope));

			if (_listScopes == null)
				_listScopes = new List<JobRuntimeScope>();

			_listScopes.Add(scope);
			return true;
		}
		private Boolean LeaveImpl(String key)
		{
			if (_listScopes == null)
				return false;

			for (Int32 i = 0; i < _listScopes.Count; i++)
			{
				if (_listScopes[i].Key == key)
				{
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
			ThreadRuntimeContext currentThreadRuntimeContext = ThreadRuntimeContext.Current;
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

		public void Dispose()
		{
			if (_jmbStack == null || _jmbStack.Count == 0)
			{
				System.Diagnostics.Debug.Assert(false, "JobMethodBuilderContext creation and disposal operations must run in LIFO order");
				return;
			}

			JobMethodBuilderContext cxt = _jmbStack.Pop();

			// this will dispose all scopes within this JobMethodbuilderContext
			// note: this object has null scopes (because it's a struct created on a stack), but the cxt read from the _listScopes has all of those
			cxt.ResetImpl();
		}
	}
}
