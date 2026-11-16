using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NewStart;
internal sealed record MediaTimeline(TimeSpan StartTime,TimeSpan EndTime,TimeSpan Position,TimeSpan MinSeekTime,TimeSpan MaxSeekTime,DateTimeOffset LastUpdatedTime);
internal sealed class AppBarMedia:IDisposable
{
    readonly Dispatcher ui;GlobalSystemMediaTransportControlsSessionManager? manager;readonly List<GlobalSystemMediaTransportControlsSession> tracked=new();GlobalSystemMediaTransportControlsSession? selected;bool disposed,userSelected;int generation;
    internal string Title {get;private set;}="No active media";
    internal string Artist {get;private set;}="";
    internal ImageSource? Artwork {get;private set;}
    internal string? Error {get;private set;}
    internal IReadOnlyList<GlobalSystemMediaTransportControlsSession> Sessions=>tracked;
    internal GlobalSystemMediaTransportControlsSession? Selected=>selected;
    internal string Source {get;private set;}="";
    internal GlobalSystemMediaTransportControlsSessionPlaybackInfo? Playback {get;private set;}
    internal MediaTimeline? Timeline {get;private set;}
    internal bool CanSeek=>Playback?.Controls.IsPlaybackPositionEnabled==true&&SeekTarget(Timeline,Timeline?.Position??TimeSpan.Zero)!=null;
    internal event Action? Changed;
    internal long Refreshes {get;private set;}
    internal AppBarMedia(Dispatcher dispatcher){ui=dispatcher;_=Initialize();}
    async Task Initialize()
    {
        try{var result=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();if(disposed)return;manager=result;manager.SessionsChanged+=SessionsChanged;manager.CurrentSessionChanged+=CurrentChanged;Sync();}
        catch(Exception e){if(!disposed){Error="Media access unavailable: "+e.Message;Changed?.Invoke();}}
    }
    void Dispatch(Action action){if(disposed||ui.HasShutdownStarted)return;try{ui.BeginInvoke(new Action(()=>{if(!disposed)action();}));}catch(InvalidOperationException){}}
    void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,SessionsChangedEventArgs args)=>Dispatch(Sync);
    void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender,CurrentSessionChangedEventArgs args)=>Dispatch(Sync);
    static void Try(Action action){try{action();}catch{}}
    void Unsubscribe(GlobalSystemMediaTransportControlsSession session)
    {
        Try(()=>session.MediaPropertiesChanged-=PropertiesChanged);Try(()=>session.PlaybackInfoChanged-=PlaybackChanged);Try(()=>session.TimelinePropertiesChanged-=TimelineChanged);
    }
    internal static string SourceName(GlobalSystemMediaTransportControlsSession? session){try{return session?.SourceAppUserModelId??"";}catch{return "Media application";}}
    void Sync()
    {
        if(disposed)return;
        try
        {
            var next=manager?.GetSessions().ToArray()??Array.Empty<GlobalSystemMediaTransportControlsSession>();
            foreach(var session in tracked.Where(session=>!next.Contains(session)).ToArray())Unsubscribe(session);
            foreach(var session in next.Where(session=>!tracked.Contains(session)))
            {
                Try(()=>session.MediaPropertiesChanged+=PropertiesChanged);Try(()=>session.PlaybackInfoChanged+=PlaybackChanged);Try(()=>session.TimelinePropertiesChanged+=TimelineChanged);
            }
            tracked.Clear();tracked.AddRange(next);
            if(selected==null||!tracked.Contains(selected))userSelected=false;
            var current=manager?.GetCurrentSession();
            var choice=userSelected?selected:current!=null&&tracked.Contains(current)?current:tracked.FirstOrDefault();
            if(choice!=selected){++generation;selected=choice;Clear();}Error=null;_=Refresh();
        }
        catch(Exception error)
        {
            foreach(var session in tracked)Unsubscribe(session);tracked.Clear();selected=null;userSelected=false;++generation;Clear();Error="Media sessions unavailable: "+error.Message;Changed?.Invoke();
        }
    }
    void PropertiesChanged(GlobalSystemMediaTransportControlsSession sender,MediaPropertiesChangedEventArgs args)=>Dispatch(()=>{if(sender==selected)_=Refresh();});
    void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender,PlaybackInfoChangedEventArgs args)=>Dispatch(()=>{if(sender==selected){ReadState(sender);Changed?.Invoke();}});
    void TimelineChanged(GlobalSystemMediaTransportControlsSession sender,TimelinePropertiesChangedEventArgs args)=>Dispatch(()=>{if(sender==selected){ReadTimeline(sender);Changed?.Invoke();}});
    void ReadTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        try{var timeline=session.GetTimelineProperties();Timeline=new(timeline.StartTime,timeline.EndTime,timeline.Position,timeline.MinSeekTime,timeline.MaxSeekTime,timeline.LastUpdatedTime);}catch{Timeline=null;}
    }
    void ReadState(GlobalSystemMediaTransportControlsSession session)
    {
        Source=SourceName(session);try{Playback=session.GetPlaybackInfo();}catch{Playback=null;}ReadTimeline(session);
    }
    void Clear(){Title="No active media";Artist="";Source="";Artwork=null;Playback=null;Timeline=null;}
    internal void Select(GlobalSystemMediaTransportControlsSession session)
    {
        if(disposed||!tracked.Contains(session))return;userSelected=true;if(session!=selected){++generation;selected=session;Clear();}_=Refresh();
    }
    async Task Refresh()
    {
        Refreshes++;
        int version=++generation;var session=selected;
        if(session==null){Clear();Error=null;Changed?.Invoke();return;}
        ReadState(session);Changed?.Invoke();
        try
        {
            var properties=await session.TryGetMediaPropertiesAsync();if(disposed||version!=generation)return;
            Title=string.IsNullOrWhiteSpace(properties.Title)?SourceName(session):properties.Title;Artist=properties.Artist;ReadState(session);Artwork=null;Error=null;Changed?.Invoke();
            if(properties.Thumbnail!=null)
            {
                try
                {
                    using var stream=await properties.Thumbnail.OpenReadAsync();
                    if(stream.Size>8*1024*1024)return;
                    using var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync((uint)stream.Size);var bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);
                    using var memory=new System.IO.MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=160;bitmap.StreamSource=memory;bitmap.EndInit();bitmap.Freeze();
                    if(!disposed&&version==generation){Artwork=bitmap;Changed?.Invoke();}
                }catch{ /* Artwork is optional; retain the text and generic music glyph. */ }
            }
        }catch(Exception e){if(!disposed&&version==generation){Error="Media details unavailable: "+e.Message;Title=SourceName(session);Artist="";Artwork=null;ReadState(session);Changed?.Invoke();}}
    }
    internal async Task Command(string command)
    {
        var session=selected;if(disposed||session==null||!tracked.Contains(session))return;
        try
        {
            var controls=session.GetPlaybackInfo().Controls;
            bool enabled=command switch{"previous"=>controls.IsPreviousEnabled,"next"=>controls.IsNextEnabled,"play"=>controls.IsPlayEnabled,"pause"=>controls.IsPauseEnabled,"toggle"=>controls.IsPlayPauseToggleEnabled,_=>false};
            if(!enabled){if(session==selected){Error="This media action is not currently supported.";Changed?.Invoke();}return;}
            bool accepted=command switch{"previous"=>await session.TrySkipPreviousAsync(),"next"=>await session.TrySkipNextAsync(),"play"=>await session.TryPlayAsync(),"pause"=>await session.TryPauseAsync(),_=>await session.TryTogglePlayPauseAsync()};
            if(!disposed&&session==selected){Error=accepted?null:"The media application did not accept this command.";ReadState(session);Changed?.Invoke();}
        }catch(Exception e){if(!disposed&&session==selected){Error=e.Message;Changed?.Invoke();}}
    }
    internal static long? SeekTarget(MediaTimeline? timeline,TimeSpan requested)
    {
        if(timeline==null||timeline.EndTime<=timeline.StartTime)return null;
        long minimum=Math.Max(timeline.StartTime.Ticks,timeline.MinSeekTime.Ticks),maximum=Math.Min(timeline.EndTime.Ticks,timeline.MaxSeekTime.Ticks);
        return maximum>minimum?Math.Clamp(requested.Ticks,minimum,maximum):null;
    }
    internal async Task Seek(TimeSpan position)
    {
        var session=selected;if(disposed||session==null||!tracked.Contains(session))return;
        try
        {
            ReadState(session);var target=SeekTarget(Timeline,position);
            if(Playback?.Controls.IsPlaybackPositionEnabled!=true||target==null){Error="The media application is not reporting a seekable playback range.";Changed?.Invoke();return;}
            bool accepted=await session.TryChangePlaybackPositionAsync(target.Value);
            if(!disposed&&session==selected){Error=accepted?null:"The media application did not accept the playback position.";ReadState(session);Changed?.Invoke();}
        }catch(Exception error){if(!disposed&&session==selected){Error=error.Message;Changed?.Invoke();}}
    }
    public void Dispose()
    {
        disposed=true;++generation;
        if(manager!=null){Try(()=>manager.SessionsChanged-=SessionsChanged);Try(()=>manager.CurrentSessionChanged-=CurrentChanged);}
        foreach(var session in tracked)Unsubscribe(session);tracked.Clear();selected=null;Clear();Changed=null;manager=null;
    }
}
