using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace NewStart;
// Runs only inside the existing killable MTA capability worker.
internal static class ApplicationMenuDiscovery
{
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
    sealed record Node(AutomationElement Element,int Depth,string[] Path,bool Menu,bool TitleBar);
    internal static CapabilityReply Process(CapabilityRequest request)
    {
        var selected=request.Action;
        if(selected==null||selected.CategoryRuntimeId is not {Length:>0 and <=64}||selected.CategoryName.Length==0||selected.MenuPath?.Length>5||!CapabilityWorker.SameWindow(request)||!CapabilityWorker.ForegroundAllowed(request))return Failure("The application or menu selection changed.");
        var app=ActiveApplication.Read(new IntPtr(request.Window));if(app==null)return Failure("The application window has changed.");app=ApplicationIdentity.Enrich(app);
        var root=AutomationElement.FromHandle(app.Handle);var category=Find(root,element=>element.GetRuntimeId().SequenceEqual(selected.CategoryRuntimeId)&&element.Current.Name==selected.CategoryName&&element.Current.ControlType==ControlType.MenuItem);
        if(category==null||!Enabled(category)||!category.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var value))return Failure("This application no longer exposes that menu.");
        var pattern=(ExpandCollapsePattern)value;if(pattern.Current.ExpandCollapseState==ExpandCollapseState.LeafNode)return Failure("The selected item is not an expandable application menu.");
        var previous=Native.GetForegroundWindow();ApplicationIdentity.GetWindowThreadProcessId(previous,out uint previousPid);
        bool opened=false,commandExecuted=false;int inspected=0;
        try
        {
            if(!CapabilityWorker.SameWindow(request)||!CapabilityWorker.ForegroundAllowed(request))return Failure("Focus changed; menu opening cancelled.");
            if(!CapabilityWorker.Activate(request))return Failure("Windows did not allow activating this application.");
            var previousPopups=PopupWindows(app).ToHashSet();
            if(pattern.Current.ExpandCollapseState!=ExpandCollapseState.Expanded){opened=true;pattern.Expand();}
            var menuRoots=WaitRoots(app,category,previousPopups);
            if(request.Operation=="openMenu")
            {
                var actions=Read(menuRoots,selected.Category,selected.CategoryRuntimeId,selected.CategoryName,Array.Empty<string>(),out inspected);
                return new(actions,actions.Count>0?selected.Label+" menu":"The application did not expose available commands in this menu.",actions.Count>0,inspected);
            }
            if(selected.MenuPath is not {Length:>0})return Failure("The menu command does not have a verified path.");
            for(int index=0;index<selected.MenuPath.Length;index++)
            {
                string name=selected.MenuPath[index];if(string.IsNullOrWhiteSpace(name)||name.Length>100)return Failure("Invalid application menu path.");
                var matches=Items(menuRoots).Where(element=>Enabled(element)&&element.Current.Name==name).GroupBy(element=>string.Join(",",element.GetRuntimeId())).Select(group=>group.First()).Take(2).ToArray();
                if(matches.Length!=1)return Failure("The menu command changed or is ambiguous. Refresh the menu.");
                var element=matches[0];bool last=index==selected.MenuPath.Length-1;
                if(!CapabilityWorker.SameWindow(request)||!TargetForeground(app))return Failure("Focus changed; menu action cancelled.");
                if(!last||selected.Method=="Expand")
                {
                    if(!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var sub))return Failure("The application no longer exposes this submenu.");
                    var existing=PopupWindows(app).ToHashSet();((ExpandCollapsePattern)sub).Expand();menuRoots=WaitRoots(app,element,existing);
                    if(last)
                    {
                        var actions=Read(menuRoots,selected.Category,selected.CategoryRuntimeId,selected.CategoryName,selected.MenuPath,out inspected);
                        return new(actions,actions.Count>0?selected.Label+" submenu":"The application did not expose available submenu commands.",actions.Count>0,inspected);
                    }
                    continue;
                }
                if(element.Current.ControlType.Id!=selected.ControlType||element.Current.Name!=selected.Name)return Failure("The menu command changed. Refresh the menu.");
                switch(selected.Method)
                {
                    case "Invoke":if(!element.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))return Failure("This menu command is no longer invokable.");((InvokePattern)invoke).Invoke();break;
                    case "Toggle":if(!element.TryGetCurrentPattern(TogglePattern.Pattern,out var toggle))return Failure("This menu command is no longer toggleable.");((TogglePattern)toggle).Toggle();break;
                    case "MSAA":if(!LegacyCapability.Invoke(new IntPtr(element.Current.NativeWindowHandle),element.Current.Name))return Failure("The legacy menu command was not accepted.");break;
                    default:return Failure("The menu action method is unsupported.");
                }
                commandExecuted=true;return new(new(),"Menu command accepted by the application.",true,inspected);
            }
            return Failure("The menu path is unavailable.");
        }
        catch(Exception error) when(error is not OutOfMemoryException){return Failure("Application menu access is unavailable ("+error.GetType().Name+").");}
        finally
        {
            if(opened){try{pattern.Collapse();}catch{}}
            // Keep application dialogs/focus after a command; only inspection returns focus to New Start.
            if(!commandExecuted&&previousPid==request.Caller&&TargetForeground(app)&&ActiveApplication.IsWindow(previous))Native.SetForegroundWindow(previous);
        }
    }
    static CapabilityReply Failure(string message)=>new(new(),message);
    static bool Enabled(AutomationElement element){try{return element.Current.IsEnabled&&!element.Current.IsOffscreen&&!element.Current.IsPassword;}catch{return false;}}
    static bool TargetForeground(ActiveWindow app)
    {
        var window=Native.GetForegroundWindow();if(window==app.Handle)return true;ApplicationIdentity.GetWindowThreadProcessId(window,out uint pid);
        if(pid!=app.ProcessId&&(app.ContentProcessId==0||pid!=app.ContentProcessId))return false;
        if(GetAncestor(window,3)==app.Handle)return true;
        for(int index=0;index<12&&window!=IntPtr.Zero;index++){window=GetWindow(window,4);if(window==app.Handle)return true;}return false;
    }
    static IntPtr[] PopupWindows(ActiveWindow app)
    {
        var result=new List<IntPtr>();Native.EnumWindows((window,_)=>{if(window==app.Handle||!Native.IsWindowVisible(window))return true;ApplicationIdentity.GetWindowThreadProcessId(window,out uint pid);if(pid==app.ProcessId||app.ContentProcessId!=0&&pid==app.ContentProcessId)result.Add(window);return true;},IntPtr.Zero);return result.Take(16).ToArray();
    }
    static List<AutomationElement> WaitRoots(ActiveWindow app,AutomationElement expanded,HashSet<IntPtr> previous)
    {
        var clock=Stopwatch.StartNew();var roots=new List<AutomationElement>();
        while(clock.ElapsedMilliseconds<350)
        {
            roots.Clear();roots.Add(expanded);
            foreach(var window in PopupWindows(app).Where(window=>!previous.Contains(window)))try{roots.Add(AutomationElement.FromHandle(window));}catch{}
            if(Items(roots).Any())return roots;
            Thread.Sleep(25);
        }
        return roots;
    }
    static List<ApplicationCapability> Read(IEnumerable<AutomationElement> roots,string category,int[] categoryId,string categoryName,string[] prefix,out int inspected)
    {
        var actions=new List<ApplicationCapability>();var seen=new HashSet<string>();inspected=0;
        foreach(var root in roots)
        {
            var queue=new Queue<Node>();queue.Enqueue(new(root,0,prefix,false,false));var clock=Stopwatch.StartNew();
            while(queue.Count>0&&inspected<180&&clock.ElapsedMilliseconds<700)
            {
                var node=queue.Dequeue();inspected++;bool menu=node.Menu,title=node.TitleBar;var path=node.Path;
                try
                {
                    var state=node.Element.Current;title|=state.ControlType==ControlType.TitleBar;menu|=state.ControlType==ControlType.Menu;
                    if(node.Depth>0&&!title&&state.ControlType==ControlType.MenuItem&&Enabled(node.Element))
                    {
                        string name=state.Name??"";if(name.Length is >0 and <=100)
                        {
                            var id=node.Element.GetRuntimeId();string key=string.Join(",",id);string method="";
                            if(node.Element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out var expand)&&((ExpandCollapsePattern)expand).Current.ExpandCollapseState!=ExpandCollapseState.LeafNode)method="Expand";
                            else if(node.Element.TryGetCurrentPattern(InvokePattern.Pattern,out _))method="Invoke";
                            else if(node.Element.TryGetCurrentPattern(TogglePattern.Pattern,out _))method="Toggle";
                            else if(LegacyCapability.CanInvoke(new IntPtr(state.NativeWindowHandle),name))method="MSAA";
                            if(method.Length>0&&seen.Add(key))
                            {
                                var commandPath=path.Concat(new[]{name}).ToArray();string label=string.Join(" › ",commandPath.Skip(prefix.Length).Select(CapabilityRegistry.Normalise));
                                actions.Add(new(CapabilityRegistry.Kind(name)??CapabilityKind.Additional,label,method,"Application menu",id,name,state.ControlType.Id,category,false,commandPath,categoryId,categoryName));
                            }
                            if(method=="Expand")path=path.Concat(new[]{name}).ToArray();
                        }
                    }
                }catch(Exception error) when(error is not OutOfMemoryException){}
                AddChildren(node.Element,node.Depth,path,menu,title,queue);
            }
        }
        return actions.GroupBy(action=>string.Join("|",action.MenuPath??Array.Empty<string>())).Select(group=>group.First()).Take(40).ToList();
    }
    static IEnumerable<AutomationElement> Items(IEnumerable<AutomationElement> roots)
    {
        var result=new List<AutomationElement>();int count=0;var clock=Stopwatch.StartNew();
        foreach(var root in roots)
        {
            var queue=new Queue<Node>();queue.Enqueue(new(root,0,Array.Empty<string>(),false,false));
            while(queue.Count>0&&count++<180&&clock.ElapsedMilliseconds<600)
            {
                var node=queue.Dequeue();try{if(node.Depth>0&&node.Element.Current.ControlType==ControlType.MenuItem&&Enabled(node.Element))result.Add(node.Element);}catch{}
                AddChildren(node.Element,node.Depth,Array.Empty<string>(),false,false,queue);
            }
        }
        return result;
    }
    static void AddChildren(AutomationElement element,int depth,string[] path,bool menu,bool title,Queue<Node> queue)
    {
        if(depth>=7)return;
        try{var child=TreeWalker.ControlViewWalker.GetFirstChild(element);int siblings=0;while(child!=null&&siblings++<60&&queue.Count<180){queue.Enqueue(new(child,depth+1,path,menu,title));child=TreeWalker.ControlViewWalker.GetNextSibling(child);}}catch{}
    }
    static AutomationElement? Find(AutomationElement root,Func<AutomationElement,bool> condition)
    {
        var queue=new Queue<Node>();queue.Enqueue(new(root,0,Array.Empty<string>(),false,false));int count=0;var clock=Stopwatch.StartNew();
        while(queue.Count>0&&count++<300&&clock.ElapsedMilliseconds<900){var node=queue.Dequeue();try{if(condition(node.Element))return node.Element;}catch{}AddChildren(node.Element,node.Depth,Array.Empty<string>(),false,false,queue);}return null;
    }
}
