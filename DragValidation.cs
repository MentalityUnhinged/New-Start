using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace NewStart;
internal static class DragValidation
{
    internal static void Run(Dictionary<string,bool> checks)
    {
        var horizontal=new[]{new Rect(100,20,40,40),new Rect(150,20,40,40),new Rect(200,20,40,40)};var vertical=horizontal.Select(rect=>new Rect(rect.Top,rect.Left,rect.Height,rect.Width)).ToArray();
        checks["DragEmptyDockInsertionSafe"]=DockDragGeometry.InsertionIndex(Array.Empty<Rect>(),new Point(50,50),false)==0&&DockDragGeometry.InsertionPosition(Array.Empty<Rect>(),0,false)==0;
        checks["DragHorizontalLeadingAndTrailingSlots"]=DockDragGeometry.InsertionIndex(horizontal,new Point(80,0),false)==0&&DockDragGeometry.InsertionIndex(horizontal,new Point(250,0),false)==3;
        checks["DragHorizontalMidpointChangesSlotOnce"]=DockDragGeometry.InsertionIndex(horizontal,new Point(120,0),false)==0&&DockDragGeometry.InsertionIndex(horizontal,new Point(120.01,0),false)==1&&DockDragGeometry.InsertionIndex(horizontal,new Point(169.99,0),false)==1&&DockDragGeometry.InsertionIndex(horizontal,new Point(170.01,0),false)==2;
        checks["DragHorizontalIgnoresVerticalPointer"]=DockDragGeometry.InsertionIndex(horizontal,new Point(180,-10000),false)==DockDragGeometry.InsertionIndex(horizontal,new Point(180,10000),false);
        checks["DragVerticalUsesSameMidpointPolicy"]=DockDragGeometry.InsertionIndex(vertical,new Point(0,120),true)==0&&DockDragGeometry.InsertionIndex(vertical,new Point(0,120.01),true)==1&&DockDragGeometry.InsertionIndex(vertical,new Point(0,250),true)==3;
        checks["DragVerticalIgnoresHorizontalPointer"]=DockDragGeometry.InsertionIndex(vertical,new Point(-10000,180),true)==DockDragGeometry.InsertionIndex(vertical,new Point(10000,180),true);
        checks["DragInsertionMarkerUsesActualSlotEdges"]=new[]{0,1,2,3}.Select(index=>DockDragGeometry.InsertionPosition(horizontal,index,false)).SequenceEqual(new[]{100d,150d,200d,240d})&&new[]{0,1,2,3}.Select(index=>DockDragGeometry.InsertionPosition(vertical,index,true)).SequenceEqual(new[]{100d,150d,200d,240d});
        checks["DragInsertionMarkerClampsOutOfRangeSlots"]=DockDragGeometry.InsertionPosition(horizontal,-10,false)==100&&DockDragGeometry.InsertionPosition(horizontal,30,false)==240;
        var excluded=new[]{horizontal[0],horizontal[2]};int moveMiddle=DockDragGeometry.InsertionIndex(excluded,new Point(180,0),false);checks["DragExcludedIconDoesNotOffsetInsertion"]=moveMiddle==1&&DockDragGeometry.InsertionPosition(excluded,moveMiddle,false)==200;
        var footprint=new Rect(50,50,300,50);
        checks["DragMinorPointerMovementDoesNotRemovePin"]=!DockDragGeometry.IsRemoval(new Point(30,70),footprint,48)&&!DockDragGeometry.IsRemoval(new Point(370,70),footprint,48);
        checks["DragRemovalThresholdBoundaryIsInclusive"]=!DockDragGeometry.IsRemoval(new Point(2,75),footprint,48)&&!DockDragGeometry.IsRemoval(new Point(398,75),footprint,48)&&!DockDragGeometry.IsRemoval(new Point(100,2),footprint,48)&&!DockDragGeometry.IsRemoval(new Point(100,148),footprint,48);
        checks["DragOutsideAnyRemovalEdgeIsDistinct"]=DockDragGeometry.IsRemoval(new Point(1.99,75),footprint,48)&&DockDragGeometry.IsRemoval(new Point(398.01,75),footprint,48)&&DockDragGeometry.IsRemoval(new Point(100,1.99),footprint,48)&&DockDragGeometry.IsRemoval(new Point(100,148.01),footprint,48);
        checks["DragReturningToDockCancelsRemovalIndication"]=DockDragGeometry.IsRemoval(new Point(450,75),footprint,48)&&!DockDragGeometry.IsRemoval(new Point(100,75),footprint,48);
        checks["DragNegativeRemovalThresholdClampsToZero"]=!DockDragGeometry.IsRemoval(new Point(50,50),footprint,-20)&&DockDragGeometry.IsRemoval(new Point(49.99,75),footprint,-20);
        checks["DragGeometryDoesNotMutateDockFootprint"]=footprint==new Rect(50,50,300,50);
        checks["DragDisplayScalePreservesInsertionAndThreshold"]=new[]{1d,1.25,1.5,2d}.All(scale=>
        {
            var scaled=horizontal.Select(rect=>Scale(rect,scale)).ToArray();return DockDragGeometry.InsertionIndex(scaled,new Point(180*scale,0),false)==2&&DockDragGeometry.IsRemoval(new Point(450*scale,75*scale),Scale(footprint,scale),48*scale)&&!DockDragGeometry.IsRemoval(new Point(30*scale,75*scale),Scale(footprint,scale),48*scale);
        });
        var leftMonitor=horizontal.Select(rect=>new Rect(rect.Left-1280,rect.Top-720,rect.Width,rect.Height)).ToArray();checks["DragNegativeMonitorCoordinatesSupported"]=DockDragGeometry.InsertionIndex(leftMonitor,new Point(180-1280,0),false)==2&&DockDragGeometry.InsertionPosition(leftMonitor,2,false)==200-1280;
        string[] apps={@"C:\DragFixture\A.exe",@"C:\DragFixture\B.exe",@"C:\DragFixture\C.exe"};var preferences=new Preferences{FollowWindowsTaskbarPins=false,Pins=apps.ToList()};var model=new DockPins(preferences,()=>Array.Empty<string>());
        int toEnd=DockDragGeometry.InsertionIndex(new[]{horizontal[1],horizontal[2]},new Point(250,20),false);model.Place(apps[0],toEnd);checks["DragDropOrderMatchesTrailingInsertionIndicator"]=model.All().SequenceEqual(new[]{apps[1],apps[2],apps[0]});
        model.Place(apps[0],0);int toMiddle=DockDragGeometry.InsertionIndex(new[]{horizontal[1],horizontal[2]},new Point(200,20),false);model.Place(apps[0],toMiddle);checks["DragDropOrderMatchesMiddleInsertionIndicator"]=model.All().SequenceEqual(new[]{apps[1],apps[0],apps[2]});
        var restored=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;restored.Normalize();checks["DragReorderedPinListPersists"]=new DockPins(restored,()=>Array.Empty<string>()).All().SequenceEqual(model.All());
        var beforeCancellation=model.All().ToArray();_ =DockDragGeometry.InsertionIndex(horizontal,new Point(0,0),false);checks["DragCancelledGeometryDoesNotReorderPins"]=model.All().SequenceEqual(beforeCancellation);
    }
    static Rect Scale(Rect rect,double scale)=>new(rect.X*scale,rect.Y*scale,rect.Width*scale,rect.Height*scale);
}
