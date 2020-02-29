using System;
using System.Collections.Generic;
using System.Reflection;
using Android.Content;
using Android.Views;
using Android.Util;
using Android.App;
using FLabelRenderer = Xamarin.Forms.Platform.Android.FastRenderers.LabelRenderer;
using ABuildVersionCodes = Android.OS.BuildVersionCodes;
using ABuild = Android.OS.Build;
using AView = Android.Views.View;
using ARelativeLayout = Android.Widget.RelativeLayout;
//using AToolbar = Android.Support.V7.Widget.Toolbar;
using System.Threading;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Xamarin.Forms.Internals;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Xamarin.Forms.Platform.Android
{
	public interface IAnticipatable
	{
		object Get();
	}

	public sealed class AndroidAnticipator
	{
		static class Key
		{
			internal struct ClassConstruction : IAnticipatable
			{
				private Type _type;

				public ClassConstruction(Type type)
				{
					_type = type;
				}

				object IAnticipatable.Get()
				{
					RuntimeHelpers.RunClassConstructor(_type.TypeHandle);
					return null;
				}

				public override string ToString()
				{
					return ".cctor=" + _type.Name;
				}
			}

			internal struct SdkVersion : IAnticipatable
			{
				object IAnticipatable.Get()
					=> ABuild.VERSION.SdkInt;

				public override string ToString()
					=> $"{nameof(SdkVersion)}";
			}

			internal struct IdedResourceExists : IAnticipatable
			{
				readonly internal Context Context;
				readonly internal int Id;

				internal IdedResourceExists(Context context, int id)
				{
					Context = context;
					Id = id;
				}

				object IAnticipatable.Get()
				{
					if (Id == 0)
						return false;

					using (var value = new TypedValue())
						return Context.Theme.ResolveAttribute(Id, value, true);
				}

				public override string ToString()
					=> $"{nameof(IdedResourceExists)}, id={Id}, '{ResourceName(Id)}'";
			}

			internal struct NamedResourceExists : IAnticipatable
			{
				readonly internal Context Context;
				readonly internal string Name;
				readonly internal string Type;

				internal NamedResourceExists(Context context, string name, string type)
				{
					Context = context;
					Name = name;
					Type = type;
				}

				object IAnticipatable.Get()
				{
					var id = Context.Resources.GetIdentifier(Name, Type, Context.PackageName);
					if (id == 0)
						return false;

					using (var value = new TypedValue())
						return Context.Theme.ResolveAttribute(id, value, true);
				}

				public override string ToString()
					=> $"{nameof(NamedResourceExists)}, name='{Name}', type='{Type}'";
			}

			internal struct InflateResource : IAnticipatable
			{
				readonly internal Context Context;
				readonly internal int Id;

				internal InflateResource(Context context, int id)
				{
					Context = context;
					Id = id;
				}

				object IAnticipatable.Get()
				{
					if (Id == 0)
						return null;

					var activity = (Activity)Context;

					var layoutInflator = activity.LayoutInflater;
					return layoutInflator.Inflate(Id, null);
				}

				public override string ToString()
					=> $"{nameof(InflateResource)}, id={Id}, '{ResourceName(Id)}'";
			}

			internal struct InflateIdedResourceFromContext : IAnticipatable
			{
				readonly internal Context Context;
				readonly internal int Id;

				internal InflateIdedResourceFromContext(Context context, int id)
				{
					Context = context;
					Id = id;
				}

				object IAnticipatable.Get()
				{
					if (Id == 0)
						return null;

					var layoutInflator = LayoutInflater.FromContext(Context);
					return layoutInflator.Inflate(Id, null);
				}

				public override string ToString()
					=> $"{nameof(InflateResource)}, id={Id}, '{ResourceName(Id)}'";
			}

			internal struct ActivateView :
				IAnticipatable, IEquatable<ActivateView>
			{
				readonly internal Context Context;
				readonly internal Type Type;
				readonly internal Func<Context, object> Factory;

				internal ActivateView(Context context, Type type, Func<Context, object> activator = null)
				{
					Context = context;
					Type = type;
					Factory = activator;
				}

				object IAnticipatable.Get()
				{
					if (Factory == null)
						return Activator.CreateInstance(Type, Context);

					return Factory(Context);
				}

				public override int GetHashCode()
					=> Context.GetHashCode() ^ Type.GetHashCode();
				public bool Equals(ActivateView other)
					=> other.Context == Context && other.Type == Type;
				public override bool Equals(object other)
					=> other is ActivateView ? Equals((ActivateView)other) : false;
				public override string ToString()
					=> $"{nameof(ActivateView)}, Type={Type.GetTypeInfo().Name}";
			}
		}

		public static void Initialize(ContextWrapper context)
		{
			if (context == null)
				throw new ArgumentNullException(nameof(context));

			s_singleton.AnticipateValue(new Key.SdkVersion());

			s_singleton.AnticipateValue(new Key.ClassConstruction(typeof(Resource.Layout)));
			s_singleton.AnticipateValue(new Key.ClassConstruction(typeof(Resource.Attribute)));

			//s_singleton.AnticipateAllocation(new Key.ActivateView(context, typeof(AToolbar), o => new AToolbar(o)));
			s_singleton.AnticipateAllocation(new Key.ActivateView(context.BaseContext, typeof(ARelativeLayout), o => new ARelativeLayout(o)));
			s_singleton.AnticipateAllocation(new Key.InflateResource(context, FormsAppCompatActivity.ToolbarResource));

			s_singleton.AnticipateValue(new Key.IdedResourceExists(context, global::Android.Resource.Attribute.ColorAccent));
			s_singleton.AnticipateValue(new Key.NamedResourceExists(context, "colorAccent", "attr"));

			s_singleton.AnticipateAllocation(new Key.InflateIdedResourceFromContext(context, Resource.Layout.FlyoutContent));

			//s_singleton.AnticipateAllocation(new Key.ActivateView(context, typeof(FLabelRenderer)));
			//s_singleton.AnticipateAllocation(new Key.ActivateView(context, typeof(PageRenderer)));

			//s_threadPool.Schedule(() => {
			//	new PageRenderer(s_context);
			//	new FLabelRenderer(s_context);
			//	new FButtonRenderer(s_context);
			//	new FImageRenderer(s_context);
			//	new FFrameRenderer(s_context);
			//	new ListViewRenderer(s_context);
			//	new AFragment();
			//	new DummyDrawable();
			//});
		}

		public static void Join()
			=> s_singleton.Dispose();

		internal static ABuildVersionCodes SdkVersion
			=> (ABuildVersionCodes)s_singleton.Get(new Key.SdkVersion());

		internal static bool IdedResourceExists(Context context, int id)
			=> (bool)s_singleton.Get(new Key.IdedResourceExists(context, id));

		internal static bool NamedResourceExists(Context context, string name, string type)
			=> (bool)s_singleton.Get(new Key.NamedResourceExists(context, name, type));

		internal static AView InflateResource(Context context, int id)
			=> (AView)s_singleton.Get(new Key.InflateResource(context, id));

		internal static AView ActivateView(Context context, Type type)
			=> (AView)s_singleton.Get(new Key.ActivateView(context, type));

		static string ResourceName(int id)
			=> id != 0 && s_resourceNames.TryGetValue(id, out var name) ? name : id.ToString();

		static Dictionary<int, string> s_resourceNames = new Dictionary<int, string>
		{
			[FormsAppCompatActivity.ToolbarResource] = nameof(FormsAppCompatActivity.ToolbarResource),
			[global::Android.Resource.Attribute.ColorAccent] = nameof(global::Android.Resource.Attribute.ColorAccent),
			[Resource.Layout.FlyoutContent] = nameof(Resource.Layout.FlyoutContent),
		};

		static Anticipator s_singleton = new Anticipator();
	}

	internal class Scheduler
	{
		private static readonly TimeSpan LoopTimeOut = TimeSpan.FromSeconds(5.0);
		private readonly Thread _thread;
		private readonly AutoResetEvent _work;
		private readonly AutoResetEvent _done;
		private readonly ConcurrentQueue<Action> _actions;

		internal Scheduler()
		{
			_actions = new ConcurrentQueue<Action>();
			_work = new AutoResetEvent(false);
			_done = new AutoResetEvent(false);
			_thread = new Thread(new ParameterizedThreadStart(Loop));
			_thread.Start(_work);
		}

		private void Loop(object argument)
		{
			var autoResetEvent = (AutoResetEvent)argument;

			while (autoResetEvent.WaitOne(LoopTimeOut))
			{
				while (_actions.Count > 0)
				{
					Action action;
					if (_actions.TryDequeue(out action))
						action();
				}
			}

			_done.Set();
		}

		internal void Schedule(Action action)
		{
			if (action == null)
				throw new ArgumentNullException(nameof(action));

			_actions.Enqueue(action);
			_work.Set();
		}

		internal void Join()
			=> _done.WaitOne();
	}

	struct CpuUsage {

		const string ProcStatPath = "/proc/stat";

		struct TotalIdle
		{
			/**
			 From SO: e.g cpu 79242 0 74306 842486413 756859 6140 67701 0
			 - 1st column : user = normal processes executing in user mode
			 - 2nd column : nice = niced processes executing in user mode
			 - 3rd column : system = processes executing in kernel mode
			 - 4th column : idle = twiddling thumbs
			 - 5th column : iowait = waiting for I/O to complete
			 - 6th column : irq = servicing interrupts
			 - 7th column : softirq = servicing softirqs
			**/
			const int ProcStatColumns = 7;
			const int ProcStatIdleColumn = 3;

			public int Total;
			public int Idle;

			public TotalIdle(string procStat)
			{
				var splits = procStat.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				var counts = splits.Skip(1).Select(o => int.Parse(o)).Take(ProcStatColumns).ToArray();
				Total = counts.Sum();
				Idle = counts.Skip(ProcStatIdleColumn).First();
			}
		}

		public static int operator-(CpuUsage lhs, CpuUsage rhs)
		{
			var rhsValue = rhs._totalIdle.Value;
			var lhsValue = lhs._totalIdle.Value;

			var idle = (double)(lhsValue.Idle - rhsValue.Idle);
			var total = (double)(lhsValue.Total - rhsValue.Total);

			var idlePercentage = (int)(idle * 100 / total);
			return 100 - idlePercentage;
		}

		internal static CpuUsage Now
			=> new CpuUsage(File.ReadLines(ProcStatPath).First());

		Lazy<TotalIdle> _totalIdle;

		public CpuUsage(string procStat)
		{
			_totalIdle = new Lazy<TotalIdle>(() => new TotalIdle(procStat));
		}

		public override string ToString()
			=> $"Total={_totalIdle.Value.Total}, Idle={_totalIdle.Value.Idle}";
	}

	internal sealed class Anticipator
	{
		interface IHeapOrCache : IDisposable
		{
			void Set(object key, object value);
			bool TryGet(object key, out object value);
		}

		sealed class HeapOrCache : IDisposable
		{
			sealed class Cache : IHeapOrCache
			{
				readonly ConcurrentDictionary<object, object> _dictionary
					= new ConcurrentDictionary<object, object>();
				readonly ConcurrentDictionary<object, bool> _accessed
					= new ConcurrentDictionary<object, bool>();

				void Log(string format, params object[] arguments)
					=> Profile.WriteLog(format, arguments);

				public void Set(object key, object value)
					=> _dictionary.TryAdd(key, value);

				public bool TryGet(object key, out object value)
				{
					var result = _dictionary.TryGetValue(key, out value);
					if (!result)
						return false;

					if (_accessed.TryAdd(key, true))
						Log("CACHE HIT: {0}", key);

					return true;
				}

				public void Dispose()
				{
					foreach (var pair in _dictionary)
					{
						if (!_accessed.ContainsKey(pair.Key))
							Log("CACHE UNUSED: {0}", pair.Key);

						(pair.Value as IDisposable)?.Dispose();
					}

					_dictionary.Clear();

					Log("CACHE DISPOSED");
				}
			}

			sealed class Heap : IHeapOrCache
			{
				static ConcurrentBag<object> ActivateBag(object key)
					=> new ConcurrentBag<object>();

				readonly ConcurrentDictionary<object, object> _dictionary
					= new ConcurrentDictionary<object, object>();
				readonly Func<object, ConcurrentBag<object>> _activateBag = ActivateBag;

				void Log(string format, params object[] arguments)
					=> Profile.WriteLog(format, arguments);

				ConcurrentBag<object> Get(object key)
					=> (ConcurrentBag<object>)_dictionary.GetOrAdd(key, _activateBag);

				public void Set(object key, object value)
					=> Get(key).Add(value);

				public bool TryGet(object key, out object value)
				{
					var result = Get(key).TryTake(out value);
					Log("CACHE {0}: {1}", result ? "HIT" : "MISS", key);
					return true;
				}

				public void Dispose()
				{
					foreach (var pair in _dictionary)
					{
						foreach (var value in ((ConcurrentBag<object>)pair.Value))
						{
							Log("CACHE UNUSED: {0}", pair.Key);
							(value as IDisposable)?.Dispose();
						}
					}

					_dictionary.Clear();

					Log("HEAP DISPOSED");
				}
			}

			public static HeapOrCache ActivateCache(Scheduler scheduler) => 
				new HeapOrCache(scheduler, new Cache());

			public static HeapOrCache ActivateHeap(Scheduler scheduler) => 
				new HeapOrCache(scheduler, new Heap());

			Scheduler _scheduler;
			IHeapOrCache _heapOrCache;

			HeapOrCache(Scheduler scheduler, IHeapOrCache container)
			{
				_scheduler = scheduler;
				_heapOrCache = container;
			}

			void Log(string format, params object[] arguments)
				=> Profile.WriteLog(format, arguments);

			public object Get<T>(T key = default)
				where T : IAnticipatable
			{
				if (!_heapOrCache.TryGet(key, out var value))
					return key.Get();
				
				return value;
			}

			public void Anticipate<T>(T key = default)
				where T : IAnticipatable
			{
				_scheduler.Schedule(() =>
				{
					try
					{
						var stopwatch = new Stopwatch();
						stopwatch.Start();
						_heapOrCache.Set(key, key.Get());
						var ticks = stopwatch.ElapsedTicks;

						Log("CASHED: {0}, ms={1}", key, TimeSpan.FromTicks(ticks).Milliseconds);
					}
					catch (Exception ex)
					{
						Log("EXCEPTION: {0}: {1}", key, ex);
					}
				});
			}

			public void Dispose()
				=> _heapOrCache.Dispose();
		}

		readonly Scheduler _scheduler;
		readonly HeapOrCache _cache;
		readonly HeapOrCache _heap;
		readonly CpuUsage __cpuStart;

		internal Anticipator()
		{
			_scheduler = new Scheduler();
			_heap = HeapOrCache.ActivateHeap(_scheduler);
			_cache = HeapOrCache.ActivateCache(_scheduler);
			__cpuStart = CpuUsage.Now;
		}

		internal void AnticipateAllocation<T>(T key = default)
			where T : IAnticipatable
			=> _heap.Anticipate(key);

		internal object Allocate<T>(T key = default)
			where T : IAnticipatable
			=> _heap.Get(key);

		internal void AnticipateValue<T>(T key = default)
			where T : IAnticipatable
			=> _cache.Anticipate(key);

		internal object Get<T>(T key = default)
			where T : IAnticipatable
			=> _cache.Get(key);

		public void Dispose()
		{
			_scheduler.Join();
			_cache.Dispose();
			_heap.Dispose();

			var cpuNow = CpuUsage.Now;

			Profile.WriteLog("CPU THN {0}", __cpuStart);
			Profile.WriteLog("CPU NOW {0}", cpuNow);
			Profile.WriteLog("CPU UTIL {0}%", cpuNow - __cpuStart);
		}

	}
}
 