# Optional dock background

Enable **macOS-style dock background** in New Start settings. It is off for new and existing installations until enabled. The rounded tray uses a subtle gradient and rim, with native Windows Composition blur behind sharp foreground icons. Light/Dark/System selects the tint and built-in icon contrast. Existing menu transparency and blur sliders also control this tray; zero blur hides its blur surface completely.

The reference checked on 14 November 2026 is Apple's [macOS 27 Golden Gate design](https://www.apple.com/os/macos/), including its refined Liquid Glass appearance. Apple's [27.2.1 release note](https://support.apple.com/en-us/149718) is dated 9 November 2026. This is a Windows interpretation, not Apple's proprietary material implementation or copied Apple assets.

The tray follows top, left and bottom placement. Six DIPs of internal padding surround the normal icon bounds; screen and maximised-window clearance remain eight DIPs outside the tray. Hover and bounce can extend outside it. The native blur region is restricted to the tray, not the full animation window. Turning it off collapses the tint and hides the companion blur window. Windows transparency/accessibility restrictions and native-effect failures use an opaque fallback.

`--verify-dock-glass` checks both themes on all three edges, spacing, zero/partial/full transparency, zero/nonzero blur and removal of the surface when disabled. Reports remain local. Visual acceptance also checked light and dark tint with the smallest icon size and shared slider changes. Broader display, driver and long-duration coverage is still needed.
