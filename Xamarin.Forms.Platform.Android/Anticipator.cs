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
#if __ANDROID_29__
using AToolbar = AndroidX.AppCompat.Widget.Toolbar;
#else
using AToolbar = Android.Support.V7.Widget.Toolbar;
# endif
using System.Threading;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Xamarin.Forms.Internals;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Xamarin.Forms.Platform.Android
{
	interface IPreBuildable
	{
		object Build();
	}

	interface IPreComputable
	{
		object Compute();
	}

	public sealed class AndroidAnticipator
	{
		static class Key
		{
			internal struct ClassConstruction : IPreComputable
			{
				private Type _type;

				public ClassConstruction(Type type)
				{
					_type = type;
				}

				object IPreComputable.Compute()
				{
					RuntimeHelpers.RunClassConstructor(_type.TypeHandle);
					return null;
				}

				public override string ToString()
				{
					return ".cctor=" + _type.Name;
				}
			}

			internal struct SdkVersion : IPreComputable
			{
				object IPreComputable.Compute()
					=> ABuild.VERSION.SdkInt;

				public override string ToString()
					=> $"{nameof(SdkVersion)}";
			}

			internal struct IdedResourceExists : IPreComputable
			{
				readonly internal Context Context;
				readonly internal int Id;

				internal IdedResourceExists(Context context, int id)
				{
					Context = context;
					Id = id;
				}

				object IPreComputable.Compute()
				{
					if (Id == 0)
						return false;

					using (var value = new TypedValue())
						return Context.Theme.ResolveAttribute(Id, value, true);
				}

				public override string ToString()
					=> $"{nameof(IdedResourceExists)}, id={Id}, '{ResourceName(Id)}'";
			}

			internal struct NamedResourceExists : IPreComputable
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

				object IPreComputable.Compute()
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

			internal struct InflateResource : IPreBuildable
			{
				readonly internal Context Context;
				readonly internal int Id;

				internal InflateResource(Context context, int id)
				{
					Context = context;
					Id = id;
				}

				object IPreBuildable.Build()
				{
					if (Id == 0)
						return null;

					var layoutInflator = (Context as Activity)?.LayoutInflater ?? 
						LayoutInflater.FromContext(Context);

					return layoutInflator.Inflate(Id, null);
				}

				public override string ToString()
					=> $"{nameof(InflateResource)}, id={ResourceName(Id)}";
			}

			internal struct ActivateView :
				IPreBuildable, IEquatable<ActivateView>
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

				object IPreBuildable.Build()
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

			s_singleton.AnticipateAllocation(new Key.ActivateView(context, typeof(AToolbar), o => new AToolbar(o)));
			s_singleton.AnticipateAllocation(new Key.ActivateView(context.BaseContext, typeof(ARelativeLayout), o => new ARelativeLayout(o)));
			s_singleton.AnticipateAllocation(new Key.InflateResource(context, FormsAppCompatActivity.ToolbarResource));

			s_singleton.AnticipateValue(new Key.IdedResourceExists(context, global::Android.Resource.Attribute.ColorAccent));
			s_singleton.AnticipateValue(new Key.NamedResourceExists(context, "colorAccent", "attr"));

			s_singleton.AnticipateAllocation(new Key.InflateResource(context, Resource.Layout.FlyoutContent));

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
			=> (ABuildVersionCodes)s_singleton.Compute(new Key.SdkVersion());

		internal static bool IdedResourceExists(Context context, int id)
			=> (bool)s_singleton.Compute(new Key.IdedResourceExists(context, id));

		internal static bool NamedResourceExists(Context context, string name, string type)
			=> (bool)s_singleton.Compute(new Key.NamedResourceExists(context, name, type));

		internal static AView InflateResource(Context context, int id)
			=> (AView)s_singleton.Allocate(new Key.InflateResource(context, id));

		internal static AView ActivateView(Context context, Type type)
			=> (AView)s_singleton.Allocate(new Key.ActivateView(context, type));

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

	sealed class Anticipator
	{
		sealed class Warehouse : IDisposable
		{
			static ConcurrentBag<object> ActivateBag(object key)
				=> new ConcurrentBag<object>();

			readonly ConcurrentDictionary<object, object> _dictionary;
			readonly Func<object, ConcurrentBag<object>> _activateBag;
			readonly Scheduler _scheduler;

			internal Warehouse(Scheduler scheduler)
			{
				_activateBag = ActivateBag;
				_dictionary = new ConcurrentDictionary<object, object>();
				_scheduler = scheduler;
			}

			ConcurrentBag<object> Get(object key)
				=> (ConcurrentBag<object>)_dictionary.GetOrAdd(key, _activateBag);

			void Set(object key, object value)
				=> Get(key).Add(value);

			bool TryGet(object key, out object value)
			{
				var result = Get(key).TryTake(out value);
				Profile.WriteLog("WAREHOUSE {0}: {1}", result ? "HIT" : "MISS", key);
				return result;
			}

			public object Get<T>(T key = default)
				where T : IPreBuildable
			{
				if (!TryGet(key, out var value))
					return key.Build();

				return value;
			}

			public void Anticipate<T>(T key = default)
				where T : IPreBuildable
			{
				_scheduler.Schedule(() =>
				{
					try
					{
						var stopwatch = new Stopwatch();
						stopwatch.Start();
						Set(key, key.Build());
						var ticks = stopwatch.ElapsedTicks;

						Profile.WriteLog("WEARHOUSED: {0}, ms={1}", key, TimeSpan.FromTicks(ticks).Milliseconds);
					}
					catch (Exception ex)
					{
						Profile.WriteLog("WEARHOUSE EXCEPTION: {0}: {1}", key, ex);
					}
				});
			}

			public void Dispose()
			{
				foreach (var pair in _dictionary)
				{
					foreach (var value in ((ConcurrentBag<object>)pair.Value))
					{
						Profile.WriteLog("WEARHOUSE UNUSED: {0}", pair.Key);
						(value as IDisposable)?.Dispose();
					}
				}

				_dictionary.Clear();

				Profile.WriteLog("WEARHOUSE DISPOSED");
			}
		}

		sealed class Cache : IDisposable
		{
			readonly ConcurrentDictionary<object, object> _dictionary;
			readonly ConcurrentDictionary<object, bool> _accessed;
			readonly Scheduler _scheduler;

			internal Cache(Scheduler scheduler)
			{
				_scheduler = scheduler;
				_accessed = new ConcurrentDictionary<object, bool>();
				_dictionary = new ConcurrentDictionary<object, object>();
			}

			void Set(object key, object value)
				=> _dictionary.TryAdd(key, value);

			bool TryGet(object key, out object value)
			{
				var result = _dictionary.TryGetValue(key, out value);
				if (!result)
					return false;

				if (_accessed.TryAdd(key, true))
					Profile.WriteLog("CACHE HIT: {0}", key);

				return true;
			}

			public object Get<T>(T key = default)
				where T : IPreComputable
			{
				if (!TryGet(key, out var value))
					return key.Compute();

				return value;
			}

			public void Anticipate<T>(T key = default)
				where T : IPreComputable
			{
				_scheduler.Schedule(() =>
				{
					try
					{
						var stopwatch = new Stopwatch();
						stopwatch.Start();
						Set(key, key.Compute());
						var ticks = stopwatch.ElapsedTicks;

						Profile.WriteLog("CACHED: {0}, ms={1}", key, TimeSpan.FromTicks(ticks).Milliseconds);
					}
					catch (Exception ex)
					{
						Profile.WriteLog("CACHE EXCEPTION: {0}: {1}", key, ex);
					}
				});
			}

			public void Dispose()
			{
				foreach (var pair in _dictionary)
				{
					if (!_accessed.ContainsKey(pair.Key))
						Profile.WriteLog("CACHE UNUSED: {0}", pair.Key);

					(pair.Value as IDisposable)?.Dispose();
				}

				_dictionary.Clear();

				Profile.WriteLog("CACHE DISPOSED");
			}
		}

		readonly Scheduler _scheduler;
		readonly Cache _cache;
		readonly Warehouse _heap;
		readonly CpuUsage __cpuStart;

		internal Anticipator()
		{
			_scheduler = new Scheduler();
			_heap = new Warehouse(_scheduler);
			_cache = new Cache(_scheduler);
			__cpuStart = CpuUsage.Now;
		}

		internal void AnticipateAllocation<T>(T key = default)
			where T : IPreBuildable
			=> _heap.Anticipate(key);

		internal object Allocate<T>(T key = default)
			where T : IPreBuildable
			=> _heap.Get(key);

		internal void AnticipateValue<T>(T key = default)
			where T : IPreComputable
			=> _cache.Anticipate(key);

		internal object Compute<T>(T key = default)
			where T : IPreComputable
			=> _cache.Get(key);

		public void Dispose()
		{
			_scheduler.Join();
			_cache.Dispose();
			_heap.Dispose();

			var cpuNow = CpuUsage.Now;

			Profile.WriteLog("ANTICIPATOR: CPU UTIL {0}%", cpuNow - __cpuStart);
		}
	}

	sealed class Scheduler
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
}
 