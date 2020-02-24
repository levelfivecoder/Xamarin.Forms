using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Forms.Internals;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Xamarin.Forms
{
	[EditorBrowsable(EditorBrowsableState.Always)]
	public class Tab : ShellSection
	{
	}

	[ContentProperty(nameof(Items))]
	[EditorBrowsable(EditorBrowsableState.Never)]
	public class ShellSection : ShellGroupItem, IShellSectionController, IPropertyPropagationController
	{
		#region PropertyKeys

		static readonly BindablePropertyKey ItemsPropertyKey =
			BindableProperty.CreateReadOnly(nameof(Items), typeof(ShellContentCollection), typeof(ShellSection), null,
				defaultValueCreator: bo => new ShellContentCollection());

		#endregion PropertyKeys

		#region IShellSectionController

		IShellSectionController ShellSectionController => this;
		readonly List<(object Observer, Action<Page> Callback)> _displayedPageObservers =
			new List<(object Observer, Action<Page> Callback)>();
		readonly List<IShellContentInsetObserver> _observers = new List<IShellContentInsetObserver>();
		Thickness _lastInset;
		double _lastTabThickness;

		event EventHandler<NavigationRequestedEventArgs> IShellSectionController.NavigationRequested
		{
			add { _navigationRequested += value; }
			remove { _navigationRequested -= value; }
		}

		event EventHandler<NavigationRequestedEventArgs> _navigationRequested;

		Page IShellSectionController.PresentedPage
		{
			get
			{
				if (Navigation.ModalStack.Count > 0)
				{
					if (Navigation.ModalStack[Navigation.ModalStack.Count - 1] is NavigationPage np)
						return np.Navigation.NavigationStack[np.Navigation.NavigationStack.Count - 1];

					return Navigation.ModalStack[0];
				}
				
				if (_navStack.Count > 1)
					return _navStack[_navStack.Count - 1];
				return ((IShellContentController)CurrentItem)?.Page;
			}
		}

		void IShellSectionController.AddContentInsetObserver(IShellContentInsetObserver observer)
		{
			if (!_observers.Contains(observer))
				_observers.Add(observer);

			observer.OnInsetChanged(_lastInset, _lastTabThickness);
		}

		void IShellSectionController.AddDisplayedPageObserver(object observer, Action<Page> callback)
		{
			_displayedPageObservers.Add((observer, callback));
			callback(DisplayedPage);
		}

		bool IShellSectionController.RemoveContentInsetObserver(IShellContentInsetObserver observer)
		{
			return _observers.Remove(observer);
		}

		bool IShellSectionController.RemoveDisplayedPageObserver(object observer)
		{
			foreach (var item in _displayedPageObservers)
			{
				if (item.Observer == observer)
				{
					return _displayedPageObservers.Remove(item);
				}
			}
			return false;
		}

		void IShellSectionController.SendInsetChanged(Thickness inset, double tabThickness)
		{
			foreach (var observer in _observers)
			{
				observer.OnInsetChanged(inset, tabThickness);
			}
			_lastInset = inset;
			_lastTabThickness = tabThickness;
		}

		async void IShellSectionController.SendPopping(Task poppingCompleted)
		{
			if (_navStack.Count <= 1)
				throw new Exception("Nav Stack consistency error");

			var page = _navStack[_navStack.Count - 1];

			_navStack.Remove(page);
			UpdateDisplayedPage();

			await poppingCompleted;

			RemovePage(page);
			SendUpdateCurrentState(ShellNavigationSource.Pop);
		}

		async void IShellSectionController.SendPoppingToRoot(Task finishedPopping)
		{
			if (_navStack.Count <= 1)
				throw new Exception("Nav Stack consistency error");

			var oldStack = _navStack;
			_navStack = new List<Page> { null };

			for (int i = 1; i < oldStack.Count; i++)
				oldStack[i].SendDisappearing();

			UpdateDisplayedPage();
			await finishedPopping;

			for (int i = 1; i < oldStack.Count; i++)
				RemovePage(oldStack[i]);

			SendUpdateCurrentState(ShellNavigationSource.PopToRoot);
		}

		[Obsolete]
		[EditorBrowsable(EditorBrowsableState.Never)]

		void IShellSectionController.SendPopped()
		{
			if (_navStack.Count <= 1)
				throw new Exception("Nav Stack consistency error");

			var last = _navStack[_navStack.Count - 1];
			_navStack.Remove(last);

			RemovePage(last);

			SendUpdateCurrentState(ShellNavigationSource.Pop);
		}

		ReadOnlyCollection<ShellContent> IShellSectionController.GetItems() => ((ShellContentCollection)Items).VisibleItems;

		[Obsolete]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IShellSectionController.SendPopping(Page page)
		{
			if (_navStack.Count <= 1)
				throw new Exception("Nav Stack consistency error");

			_navStack.Remove(page);
			SendAppearanceChanged();
		}

		[Obsolete]
		[EditorBrowsable(EditorBrowsableState.Never)]
		void IShellSectionController.SendPopped(Page page)
		{
			if (_navStack.Contains(page))
				_navStack.Remove(page);

			RemovePage(page);
			SendUpdateCurrentState(ShellNavigationSource.Pop);
		}


		event NotifyCollectionChangedEventHandler IShellSectionController.ItemsCollectionChanged
		{
			add { ((ShellContentCollection)Items).VisibleItemsChanged += value; }
			remove { ((ShellContentCollection)Items).VisibleItemsChanged -= value; }
		}

		#endregion IShellSectionController

		#region IPropertyPropagationController
		void IPropertyPropagationController.PropagatePropertyChanged(string propertyName)
		{
			PropertyPropagationExtensions.PropagatePropertyChanged(propertyName, this, Items);
		}
		#endregion

		public static readonly BindableProperty CurrentItemProperty =
			BindableProperty.Create(nameof(CurrentItem), typeof(ShellContent), typeof(ShellSection), null, BindingMode.TwoWay,
				propertyChanged: OnCurrentItemChanged);

		public static readonly BindableProperty ItemsProperty = ItemsPropertyKey.BindableProperty;

		Page _displayedPage;
		IList<Element> _logicalChildren = new List<Element>();

		ReadOnlyCollection<Element> _logicalChildrenReadOnly;

		List<Page> _navStack = new List<Page> { null };
		internal bool IsPushingModalStack { get; private set; }
		internal bool IsPoppingModalStack { get; private set; }

		public ShellSection()
		{
			(Items as INotifyCollectionChanged).CollectionChanged += ItemsCollectionChanged;
			Navigation = new NavigationImpl(this);
		}
				
		public ShellContent CurrentItem
		{
			get { return (ShellContent)GetValue(CurrentItemProperty); }
			set { SetValue(CurrentItemProperty, value); }
		}

		public IList<ShellContent> Items => (IList<ShellContent>)GetValue(ItemsProperty);

		public IReadOnlyList<Page> Stack => _navStack;

		internal override ReadOnlyCollection<Element> LogicalChildrenInternal => _logicalChildrenReadOnly ?? (_logicalChildrenReadOnly = new ReadOnlyCollection<Element>(_logicalChildren));

		Page DisplayedPage
		{
			get { return _displayedPage; }
			set
			{
				if (_displayedPage == value)
					return;
				_displayedPage = value;

				foreach (var item in _displayedPageObservers)
					item.Callback(_displayedPage);
			}
		}

		Shell Shell => Parent?.Parent as Shell;

		ShellItem ShellItem => Parent as ShellItem;

		internal static ShellSection CreateFromShellContent(ShellContent shellContent)
		{
			var shellSection = new ShellSection();

			var contentRoute = shellContent.Route;

			shellSection.Route = Routing.GenerateImplicitRoute(contentRoute);

			shellSection.Items.Add(shellContent);

			shellSection.SetBinding(TitleProperty, new Binding(nameof(Title), BindingMode.OneWay, source: shellContent));
			shellSection.SetBinding(IconProperty, new Binding(nameof(Icon), BindingMode.OneWay, source: shellContent));
			shellSection.SetBinding(FlyoutIconProperty, new Binding(nameof(FlyoutIcon), BindingMode.OneWay, source: shellContent));

			return shellSection;
		}

		internal static ShellSection CreateFromTemplatedPage(TemplatedPage page)
		{
			return CreateFromShellContent((ShellContent)page);
		}

#if DEBUG
		[Obsolete("Please dont use this in core code... its SUPER hard to debug when this happens", true)]
#endif
		public static implicit operator ShellSection(ShellContent shellContent)
		{
			return CreateFromShellContent(shellContent);
		}

#if DEBUG
		[Obsolete("Please dont use this in core code... its SUPER hard to debug when this happens", true)]
#endif
		public static implicit operator ShellSection(TemplatedPage page)
		{
			return (ShellSection)(ShellContent)page;
		}

		internal async Task GoToAsync(ShellRouteState navigationRequest, bool animate, Uri uri)
		{
			List<Page> navStack = null;
			string route = String.Empty;
			var currentRoute = navigationRequest.CurrentRoute;
			var pathParts = currentRoute.PathParts;
			IReadOnlyList<PathPart> globalRoutes = pathParts.Skip(3).ToList();

			if (pathParts == null || pathParts.Count <= 3)
			{
				await Navigation.PopToRootAsync(animate);
				return;
			}

			uri = ShellUriHandler.FormatUri(uri);
			bool replaceEntireStack = false;
			if (uri.IsAbsoluteUri)
				replaceEntireStack = true;
			else if (uri.OriginalString.StartsWith("//", StringComparison.Ordinal) || uri.OriginalString.StartsWith("\\\\", StringComparison.Ordinal))
				replaceEntireStack = true;

			int whereToStartNavigation = 0;
			
			// Pop the stack down to where it no longer matches 
			if (replaceEntireStack)
			{
				for (int i = 0; i < globalRoutes.Count; i++)
				{
					whereToStartNavigation = i;
					bool isLast = i == globalRoutes.Count - 1;
					route = globalRoutes[i].Path;
					
					navStack = BuildFlattenedNavigationStack(new List<Page>(_navStack), Navigation?.ModalStack);
					
					// if the navStack count is one that means there is nothing pushed
					if (navStack.Count == 1)
						break;
					
					Page navPage = navStack.Count > i + 1 ? navStack[i + 1] : null;

					if (navPage != null)
					{
						// if the routes don't match then pop this route off the stack
						int popCount = i + 1;

						if (Routing.GetRoute(navPage) == route)
						{
							// if the routes do match and this is the last in the loop
							// pop everything after this route
							popCount = i + 2;
							whereToStartNavigation++;
							ShellApplyParameters.ApplyParameters(new ShellLifecycleArgs((BaseShellItem)LogicalChildren[i], globalRoutes[i], currentRoute));

							// If we're not on the last loop of the stack then continue
							// otherwise pop the rest of the stack
							if (!isLast)
								continue;
						}

						IsPoppingModalStack = true;
						while (navStack.Count > popCount)
						{
							if (Navigation.ModalStack.Contains(navStack[navStack.Count - 1]))
							{
								await Navigation.PopModalAsync(false);
							}
							else if (Navigation.ModalStack.Count > 0)
							{
								await Navigation.ModalStack[Navigation.ModalStack.Count - 1].Navigation.PopAsync(false);
							}
							else
							{
								await OnPopAsync(false);
							}
							
							navStack = BuildFlattenedNavigationStack(new List<Page>(_navStack), Navigation?.ModalStack);
						}
						IsPoppingModalStack = false;

						break;
					}

					var content = Routing.GetOrCreateContent(route) as Page;
					if (content == null)
						break;

					ShellApplyParameters.ApplyParameters(new ShellLifecycleArgs((BaseShellItem)LogicalChildren[i], globalRoutes[i], currentRoute));
				}
			}
			
			List<Page> modalPageStacks = new List<Page>();
			List<Page> nonModalPageStacks = new List<Page>();

			if (Navigation?.ModalStack?.Count > 0)
				modalPageStacks.AddRange(Navigation.ModalStack);

			// populate global routes and build modal stacks
			for (int i = whereToStartNavigation; i < globalRoutes.Count; i++)
			{
				bool isLast = i == globalRoutes.Count - 1;
				route = globalRoutes[i].Path;
				var content = Routing.GetOrCreateContent(route) as Page;
				if (content == null)
					break;

				// Modal currently not hooked up to navigation service
				var isModal = (Shell.GetPresentationMode(content) & PresentationMode.Modal) == PresentationMode.Modal;
				Shell.ApplyQueryAttributes(content, globalRoutes[i].NavigationParameters, isLast);
				if (isModal)
				{
					modalPageStacks.Add(content);
				}
				else if (modalPageStacks.Count > 0)
				{
					if (modalPageStacks[modalPageStacks.Count - 1] is NavigationPage navigationPage)
						await navigationPage.Navigation.PushAsync(content);
					else
						throw new InvalidOperationException($"Shell cannot push a page to the following type: {modalPageStacks[modalPageStacks.Count - 1]}. The visible modal page needs to be a NavigationPage");
				}
				else
				{
					nonModalPageStacks.Add(content);
				}
			}

			for (int i = Navigation.ModalStack.Count; i < modalPageStacks.Count; i++)
			{
				bool isLast = i == modalPageStacks.Count - 1;
				bool isAnimated = (Shell.GetPresentationMode(modalPageStacks[i]) & PresentationMode.NotAnimated) != PresentationMode.NotAnimated;
				IsPushingModalStack = !isLast;
				await ((NavigationImpl)Navigation).PushModalAsync(modalPageStacks[i], isAnimated);
			}

			for (int i = nonModalPageStacks.Count - 1; i >= 0; i--)
			{
				bool isLast = i == nonModalPageStacks.Count - 1;
					
				if(isLast)
				{
					bool isAnimated = (Shell.GetPresentationMode(nonModalPageStacks[i]) & PresentationMode.NotAnimated) != PresentationMode.NotAnimated;
					await OnPushAsync(nonModalPageStacks[i], isAnimated);
				}
				else
					Navigation.InsertPageBefore(nonModalPageStacks[i], nonModalPageStacks[nonModalPageStacks.Count - 1]);
			}

			if (Parent?.Parent is IShellController shell)
			{
				shell.UpdateCurrentState(ShellNavigationSource.ShellSectionChanged);
			}

			List<Page> BuildFlattenedNavigationStack(List<Page> startingList, IReadOnlyList<Page> modalStack)
			{
				if (modalStack == null)
					return startingList;

				for (int i = 0; i < modalStack.Count; i++)
				{
					startingList.Add(modalStack[i]);
					for (int j = 1; j < modalStack[i].Navigation.NavigationStack.Count; j++)
					{
						startingList.Add(modalStack[i].Navigation.NavigationStack[j]);
					}
				}

				return startingList;
			}
		}

		internal void SendStructureChanged()
		{
			if (Parent?.Parent is Shell shell)
			{
				shell.SendStructureChanged();
			}
		}

		protected virtual IReadOnlyList<Page> GetNavigationStack() => _navStack;

		internal void UpdateDisplayedPage()
		{
			var stack = Stack;
			var previousPage = DisplayedPage;
			if (stack.Count > 1)
			{
				DisplayedPage = stack[stack.Count - 1];
			}
			else
			{
				IShellContentController currentItem = CurrentItem;
				DisplayedPage = currentItem?.Page;
			}

			if (previousPage != DisplayedPage)
			{
				previousPage?.SendDisappearing();
				PresentedPageAppearing();
				SendAppearanceChanged();
			}
		}

		protected override void OnChildAdded(Element child)
		{
			base.OnChildAdded(child);
			if (CurrentItem == null && ((IShellSectionController)this).GetItems().Contains(child))
				SetValueFromRenderer(CurrentItemProperty, child);

			if(CurrentItem != null)
				UpdateDisplayedPage();
		}

		protected override void OnChildRemoved(Element child)
		{
			base.OnChildRemoved(child);
			if (CurrentItem == child)
			{
				var items = ShellSectionController.GetItems();
				if (items.Count == 0)
					ClearValue(CurrentItemProperty);
				else
				{
					// We want to delay invoke this because the renderer may handle this instead
					Device.BeginInvokeOnMainThread(() =>
					{
						if (CurrentItem == null)
							SetValueFromRenderer(CurrentItemProperty, items[0]);
					});
				}
			}

			UpdateDisplayedPage();
		}

		internal override IEnumerable<Element> ChildrenNotDrawnByThisElement => Items;

		protected virtual void OnInsertPageBefore(Page page, Page before)
		{
			var index = _navStack.IndexOf(before);
			if (index == -1)
				throw new ArgumentException("Page not found in nav stack");

			var stack = _navStack.ToList();
			stack.Insert(index, page);

			var allow = ((IShellController)Shell).ProposeNavigation(
				ShellNavigationSource.Insert,
				Parent as ShellItem,
				this,
				CurrentItem,
				stack,
				true
			);

			if (!allow)
				return;

			_navStack.Insert(index, page);
			AddPage(page);

			var args = new NavigationRequestedEventArgs(page, before, false)
			{
				RequestType = NavigationRequestType.Insert
			};

			_navigationRequested?.Invoke(this, args);

			SendUpdateCurrentState(ShellNavigationSource.Insert);
		}

		protected async virtual Task<Page> OnPopAsync(bool animated)
		{
			if (_navStack.Count <= 1)
				throw new InvalidOperationException("Can't pop last page off stack");

			List<Page> stack = _navStack.ToList();
			stack.Remove(stack.Last());
			var allow = ((IShellController)Shell).ProposeNavigation(
				ShellNavigationSource.Pop,
				Parent as ShellItem,
				this,
				CurrentItem,
				stack,
				true
			);

			if (!allow)
				return null;
						
			var page = _navStack[_navStack.Count - 1];
			var args = new NavigationRequestedEventArgs(page, animated)
			{
				RequestType = NavigationRequestType.Pop
			};

			PresentedPageDisappearing();
			_navStack.Remove(page);
			PresentedPageAppearing();

			_navigationRequested?.Invoke(this, args);
			if (args.Task != null)
				await args.Task;
			RemovePage(page);

			SendUpdateCurrentState(ShellNavigationSource.Pop);

			return page;
		}

		protected virtual async Task OnPopToRootAsync(bool animated)
		{
			if (_navStack.Count <= 1)
				return;

			var allow = ((IShellController)Shell).ProposeNavigation(
				ShellNavigationSource.PopToRoot,
				Parent as ShellItem,
				this,
				CurrentItem,
				null,
				true
			);

			if (!allow)
				return;

			var page = _navStack[_navStack.Count - 1];
			var args = new NavigationRequestedEventArgs(page, animated)
			{
				RequestType = NavigationRequestType.PopToRoot
			};

			_navigationRequested?.Invoke(this, args);
			var oldStack = _navStack;
			_navStack = new List<Page> { null };

			if (args.Task != null)
				await args.Task;

			for (int i = 1; i < oldStack.Count; i++)
			{
				oldStack[i].SendDisappearing();
				RemovePage(oldStack[i]);
			}

			PresentedPageAppearing();
			SendUpdateCurrentState(ShellNavigationSource.PopToRoot);
		}

		protected virtual Task OnPushAsync(Page page, bool animated)
		{
			List<Page> stack = _navStack.ToList();
			stack.Add(page);
			var allow = ((IShellController)Shell).ProposeNavigation(
				ShellNavigationSource.Push,
				ShellItem,
				this,
				CurrentItem,
				stack,
				true
			);

			if (!allow)
				return Task.FromResult(true);
						
			var args = new NavigationRequestedEventArgs(page, animated)
			{
				RequestType = NavigationRequestType.Push
			};

			PresentedPageDisappearing();
			_navStack.Add(page);
			PresentedPageAppearing();
			AddPage(page);
			_navigationRequested?.Invoke(this, args);

			SendUpdateCurrentState(ShellNavigationSource.Push);

			if (args.Task == null)
				return Task.FromResult(true);
			return args.Task;
		}

		internal async Task PopModalStackToPage(Page page, bool? animated)
		{
			try
			{
				IsPoppingModalStack = true;
				int modalStackCount = Navigation.ModalStack.Count;
				for (int i = 0; i < modalStackCount; i++)
				{
					var pageToPop = Navigation.ModalStack[Navigation.ModalStack.Count - 1];
					if (pageToPop == page)
						break;

					// indicate that we are done popping down the stack to the modal page requested
					// This is mainly used by life cycle events so they don't fire onappearing
					if(page == null && Navigation.ModalStack.Count == 1)
					{
						IsPoppingModalStack = false;
					}
					else if(Navigation.ModalStack.Count > 1 && Navigation.ModalStack[Navigation.ModalStack.Count - 2] == page)
					{
						IsPoppingModalStack = false;
					}

					bool isAnimated = animated ?? (Shell.GetPresentationMode(pageToPop) & PresentationMode.NotAnimated) != PresentationMode.NotAnimated;
					await Navigation.PopModalAsync(isAnimated);
				}

				((IShellController)Shell).UpdateCurrentState(ShellNavigationSource.ShellSectionChanged);
			}
			finally
			{
				IsPoppingModalStack = false;
			}
		}

		protected virtual void OnRemovePage(Page page)
		{
			if (!_navStack.Contains(page))
				return;

			bool currentPage = (((IShellSectionController)this).PresentedPage) == page;
			var stack = _navStack.ToList();
			stack.Remove(page);
			var allow = ((IShellController)Shell).ProposeNavigation(
				ShellNavigationSource.Remove,
				ShellItem,
				this,
				CurrentItem,
				stack,
				true
			);

			if (!allow)
				return;

			if(currentPage)
				PresentedPageDisappearing();
			_navStack.Remove(page);

			if(currentPage)
				PresentedPageAppearing();

			RemovePage(page);
			var args = new NavigationRequestedEventArgs(page, false)
			{
				RequestType = NavigationRequestType.Remove
			};
			_navigationRequested?.Invoke(this, args);

			SendUpdateCurrentState(ShellNavigationSource.Remove);
		}

		internal bool IsVisibleSection => Parent?.Parent is Shell shell && shell.CurrentItem?.CurrentItem == this;
		void PresentedPageDisappearing()
		{
			if (this is IShellSectionController sectionController)
			{				
				CurrentItem?.SendDisappearing();
				sectionController.PresentedPage?.SendDisappearing();
			}
		}

		void PresentedPageAppearing()
		{
			if (IsVisibleSection && this is IShellSectionController sectionController)
			{
				if(_navStack.Count == 1)
					CurrentItem?.SendAppearing();

				var presentedPage = sectionController.PresentedPage;
				if (presentedPage != null)
				{
					if(presentedPage.Parent == null)
					{
						presentedPage.ParentSet += OnPresentedPageParentSet;

						void OnPresentedPageParentSet(object sender, EventArgs e)
						{
							PresentedPageAppearing();
							(sender as Page).ParentSet -= OnPresentedPageParentSet;
						}
					}
					else
					{
						presentedPage.SendAppearing();
					}
				}
			}
		}

		static void OnCurrentItemChanged(BindableObject bindable, object oldValue, object newValue)
		{
			var shellSection = (ShellSection)bindable;

			if (oldValue is ShellContent oldShellItem)
				oldShellItem.SendDisappearing();

			shellSection.PresentedPageAppearing();

			if (shellSection.Parent?.Parent is IShellController shell)
			{
				shell.UpdateCurrentState(ShellNavigationSource.ShellSectionChanged);
			}

			shellSection.SendStructureChanged();
			((IShellController)shellSection?.Parent?.Parent)?.AppearanceChanged(shellSection, false);

			shellSection.UpdateDisplayedPage();
		}

		void AddPage(Page page)
		{
			if (page.Parent == null)
			{
				page.Parent = new ShellContent()
				{
					Content = page,
					Route = Routing.GetRoute(page)
				};
			}

			if (page.Parent is ShellContent content)
			{
				_logicalChildren.Add(content);
				OnChildAdded(content);
			}
			else
			{
				throw new ArgumentException($"Invalid type on parent: {page.Parent}");
			}
		}

		void ItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.NewItems != null)
			{
				foreach (Element element in e.NewItems)
					OnChildAdded(element);
			}

			if (e.OldItems != null)
			{
				foreach (Element element in e.OldItems)
					OnChildRemoved(element);
			}

			SendStructureChanged();
		}

		void RemovePage(Page page)
		{
			if (_logicalChildren.Remove(page))
				OnChildRemoved(page);
		}

		void SendAppearanceChanged() => ((IShellController)Parent?.Parent)?.AppearanceChanged(this, false);

		void SendUpdateCurrentState(ShellNavigationSource source)
		{
			if (Parent?.Parent is IShellController shell)
			{
				shell.UpdateCurrentState(source);
			}
		}

		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();

			foreach (ShellContent shellContent in Items)
			{
				SetInheritedBindingContext(shellContent, BindingContext);
			}
		}
    
		internal override void SendDisappearing()
		{
			base.SendDisappearing();
			PresentedPageDisappearing();
		}

		internal override void SendAppearing()
		{
			base.SendAppearing();
			PresentedPageAppearing();
		}

		#region Navigation Interfaces
		IShellApplyParameters ShellApplyParameters => (Parent.Parent as Shell).ShellApplyParameters;
		IShellContentCreator ShellContentCreator => (Parent.Parent as Shell).ShellContentCreator;
		IShellPartAppearing ShellPartAppearing => (Parent.Parent as Shell).ShellPartAppearing;
		#endregion

		class NavigationImpl : NavigationProxy
		{
			readonly ShellSection _owner;

			public NavigationImpl(ShellSection owner) => _owner = owner;

			protected override IReadOnlyList<Page> GetNavigationStack() => _owner.GetNavigationStack();

			protected override void OnInsertPageBefore(Page page, Page before) => _owner.OnInsertPageBefore(page, before);

			protected override Task<Page> OnPopAsync(bool animated) => _owner.OnPopAsync(animated);

			protected override Task OnPopToRootAsync(bool animated) => _owner.OnPopToRootAsync(animated);

			protected override Task OnPushAsync(Page page, bool animated) => _owner.OnPushAsync(page, animated);

			protected override void OnRemovePage(Page page) => _owner.OnRemovePage(page);

			protected override Task<Page> OnPopModal(bool animated)
			{
				if(ModalStack.Count == 1)
				{
					_owner.PresentedPageAppearing();
				}

				return base.OnPopModal(animated);
			}

			protected override Task OnPushModal(Page modal, bool animated)
			{
				if (ModalStack.Count == 0)
				{
					_owner.PresentedPageDisappearing();
				}

				return base.OnPushModal(modal, animated);
			}
		}
	}
}
