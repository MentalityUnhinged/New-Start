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
internal sealed class AppBarMedia:IDisposable
{
    readonly Dispatcher ui;GlobalSystemMediaTransportControlsSessionManager? manager;readonly List<GlobalSystemMediaTransportControlsSession> tracked=new();GlobalSystemMediaTransportControlsSession? selected;bool disposed,userSelected;int generation;
    internal string Title {get;private set;}="No active media";
    internal string Artist {get;private set;}="";
    internal ImageSource? Artwork {get;private set;}
    internal string? Error {get;private set;}
    internal IReadOnlyList<GlobalSystemMediaTransportControlsSession> Sessions=>tracked;
    internal GlobalSystemMediaTransportControlsSession? Selected=>selected;
    internal GlobalSystemMediaTransportControlsSessionPlaybackInfo? Playback {get;private set;}
    internal event Action? Changed;
    internal long Refreshes {get;private set;}
    internal AppBarMedia(Dispatcher dispatcher){ui=dispatcher;_=Initialize();}
    async Task Initialize()
    {
        try{var result=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();if(disposed)return;manager=result;manager.SessionsChanged+=SessionsChanged;manager.CurrentSessionChanged+=CurrentChanged;Sync();}
        catch(Exception e){Error="Media access unavailable: "+e.Message;Changed?.Invoke();}
    }
    void Dispatch(Action action){if(!disposed)ui.BeginInvoke(new Action(()=>{if(!disposed)action();}));}
    void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,SessionsChangedEventArgs args)=>Dispatch(Sync);
    void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender,CurrentSessionChangedEventArgs args)=>Dispatch(()=>{if(!userSelected||selected==null||!tracked.Contains(selected))selected=manager?.GetCurrentSession();_=Refresh();});
    void Sync()
    {
        foreach(var session in tracked){session.MediaPropertiesChanged-=PropertiesChanged;session.PlaybackInfoChanged-=PlaybackChanged;}
        tracked.Clear();if(manager!=null)tracked.AddRange(manager.GetSessions());
        foreach(var session in tracked){session.MediaPropertiesChanged+=PropertiesChanged;session.PlaybackInfoChanged+=PlaybackChanged;}
        if(selected==null||!tracked.Contains(selected)){userSelected=false;selected=manager?.GetCurrentSession()??tracked.FirstOrDefault();}else if(!userSelected)selected=manager?.GetCurrentSession()??selected;_=Refresh();
    }
    void PropertiesChanged(GlobalSystemMediaTransportControlsSession sender,MediaPropertiesChangedEventArgs args){if(sender==selected)Dispatch(()=>_=Refresh());}
    void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender,PlaybackInfoChangedEventArgs args){if(sender==selected)Dispatch(()=>{try{Playback=selected?.GetPlaybackInfo();Changed?.Invoke();}catch{_=Refresh();}});}
    internal void Select(GlobalSystemMediaTransportControlsSession session){userSelected=true;selected=session;_=Refresh();}
    async Task Refresh()
    {
        Refreshes++;
        int version=++generation;var session=selected;
        if(session==null){Title="No active media";Artist="";Artwork=null;Playback=null;Changed?.Invoke();return;}
        try
        {
            var properties=await session.TryGetMediaPropertiesAsync();if(disposed||version!=generation)return;
            Title=string.IsNullOrWhiteSpace(properties.Title)?session.SourceAppUserModelId:properties.Title;Artist=properties.Artist;Playback=session.GetPlaybackInfo();Artwork=null;Error=null;Changed?.Invoke();
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
        }catch(Exception e){if(!disposed&&version==generation){Error=e.Message;Title="Media unavailable";Artist="";Artwork=null;Playback=null;Changed?.Invoke();}}
    }
    internal async Task Command(string command)
    {
        var session=selected;if(session==null)return;
        try
        {
            bool accepted=command switch{"previous"=>await session.TrySkipPreviousAsync(),"next"=>await session.TrySkipNextAsync(),_=>await session.TryTogglePlayPauseAsync()};
            if(!disposed){Error=accepted?null:"The media application did not accept this command.";Changed?.Invoke();}
        }catch(Exception e){if(!disposed){Error=e.Message;Changed?.Invoke();}}
    }
    public void Dispose()
    {
        disposed=true;++generation;
        if(manager!=null){manager.SessionsChanged-=SessionsChanged;manager.CurrentSessionChanged-=CurrentChanged;}
        foreach(var session in tracked){session.MediaPropertiesChanged-=PropertiesChanged;session.PlaybackInfoChanged-=PlaybackChanged;}tracked.Clear();manager=null;
    }
}
