I’ll consolidate the entire discussion into one canonical, comprehensive ExceptionMaster project specification that you can save as Markdown and provide to Google Antigravity. It will include the original requirements, every design decision discussed, the additional diagnostic fields, NLog dependency isolation, configurable database logging, fallback behavior, aggregation, fan-out, fire-and-forget events, testing, and NuGet packaging.

I’ll also distinguish between requirements we established and implementation details that still need validation. This is important because some earlier proposed code snippets had concurrency and fallback issues that should not be carried into the final implementation.

# ExceptionMaster — Complete discussion and requirements specification

Document purpose: Canonical project requirements, architecture, implementation constraints, test plan, and delivery instructions for an AI coding agent.

## 1. Original objective

Build a reusable, enterprise-oriented exception logging library named ExceptionMaster for use across multiple existing and future .NET applications.

The library must provide a centralized way to capture, aggregate, route, and persist exceptions without allowing failures in the logging infrastructure to break the application that is being logged.

The initial logging destinations are:

* SQL database.

* Local log file.

* Console.

The system must support fallback when a destination becomes unavailable, for example:

`SQL database unavailable → file logging → console logging`

It must also support fan-out, meaning one exception can be sent to multiple independent destinations, rather than only to the first successful destination.

The library should capture repeated exceptions as aggregated records, retaining the first and most recent occurrence times and a count, instead of writing an entirely new persistent log record for every repetition.

It must be suitable for existing applications targeting classic .NET Framework, without requiring migration to .NET Core or a modern .NET runtime.

## 2. All requirements established during the discussion

Failure isolation

Logging, persistence, fallback, aggregation, and event-handler failures must never propagate back through the logging API into the calling application.

Reusable architecture

The core library must not require NLog, a database, or a logging configuration file to be installed in every consumer project.

Fan-out and fallback

Independent sinks can receive the same event. A failed sink must not prevent other configured sinks from receiving it.

Optional SQL integration

Each consuming application can configure its database connection or disable database logging entirely.

Exception aggregation

Repeated logical exceptions increase the occurrence count and update the last-seen timestamp instead of producing a separate persistent record for each occurrence.

Buffered and asynchronous operation

Support configurable buffering, batch flushing, flush intervals, and fire-and-forget notification without unnecessarily blocking latency-sensitive applications.

NuGet-ready delivery

Produce a complete Visual Studio solution, independent packages where appropriate, documented APIs, samples, and automated tests.

### Non-negotiable behavior

1. A logging failure must not become a new application exception.

2. A failed SQL sink must not prevent a file or console sink from being attempted.

3. A failed file sink must not prevent a console sink from being attempted.

4. An absent or invalid optional configuration must not crash the application.

5. An event subscriber that throws must not affect the logger or other subscribers.

6. An exception generated inside an event subscriber and sent back to ExceptionMaster must not produce an infinite recursive event chain.

7. The library must be thread-safe under concurrent calls from many threads.

8. Configuration must be customizable by the consuming application.

9. NLog must remain an optional integration, not a mandatory dependency of the core.

10. Repeated exceptions must be aggregated according to a documented, configurable policy.

## 3. Target framework and compatibility

The original target was classic .NET Framework above 4.6.0 and below .NET Core. The intended practical minimum is .NET Framework 4.6.1 or higher, while preserving compatibility with existing legacy projects.

The implementation must not silently introduce .NET Core or modern .NET-only dependencies into the classic framework package.

| Area                           | Requirement                                                                                    |
| ------------------------------ | ---------------------------------------------------------------------------------------------- |
| Core library                   | No NLog dependency                                                                             |
| Legacy compatibility           | Classic .NET Framework 4.6.1+ as the primary compatibility target                              |
| Optional broader compatibility | Evaluate .NET Standard 2.0 separately; do not claim support without compiling and testing it   |
| Language                       | Use syntax supported by the selected compiler and target framework                             |
| Modern runtime-only APIs       | Prohibited in the legacy-targeted assembly unless compatibility is explicitly verified         |
| Vendor libraries               | Do not upgrade or replace existing application dependencies merely to accommodate this library |
| Packaging                      | Explicit target frameworks and dependencies in package metadata                                |

Important clarification: .NET Standard 2.0 can support certain classic .NET Framework consumers, but it is not equivalent to targeting .NET Framework directly. In particular, compatibility with .NET Framework 4.6.1 can involve dependency and runtime issues. Antigravity must validate actual build and runtime support for each target rather than assuming that a multi-targeted project automatically works everywhere.


## 4. NLog dependency conflict — the first architectural problem

The original idea was to use NLog for SQL, file, and console logging. However, an existing application may already reference NLog, potentially at a different version, with its own targets, rules, and configuration.

A reusable library must not unexpectedly replace or modify the application's logging configuration.

### Required dependency design

Diagram options

![](data\:image/svg+xml;utf8,%3Csvg%20id%3D%22mermaid-_r_kj_%22%20width%3D%22835.381103515625%22%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20class%3D%22flowchart%22%20height%3D%22506.6000061035156%22%20viewBox%3D%224%204%20835.381103515625%20506.6000061035156%22%20role%3D%22graphics-document%20document%22%20aria-roledescription%3D%22flowchart-v2%22%3E%3Cstyle%3E%23mermaid-_r_kj_%7Bfont-family%3A%22-apple-system%22%2C%22BlinkMacSystemFont%22%2C%22Segoe%20UI%22%2C%22Roboto%22%2C%22Oxygen%22%2C%22Ubuntu%22%2C%22Cantarell%22%2C%22Helvetica%20Neue%22%2C%22Arial%22%2C%22sans-serif%22%3Bfont-size%3A14px%3Bfill%3Argb\(13%2C%2013%2C%2013\)%3B%7D%40keyframes%20edge-animation-frame%7Bfrom%7Bstroke-dashoffset%3A0%3B%7D%7D%40keyframes%20dash%7Bto%7Bstroke-dashoffset%3A0%3B%7D%7D%23mermaid-_r_kj_%20.edge-animation-slow%7Bstroke-dasharray%3A9%2C5!important%3Bstroke-dashoffset%3A900%3Banimation%3Adash%2050s%20linear%20infinite%3Bstroke-linecap%3Around%3B%7D%23mermaid-_r_kj_%20.edge-animation-fast%7Bstroke-dasharray%3A9%2C5!important%3Bstroke-dashoffset%3A900%3Banimation%3Adash%2020s%20linear%20infinite%3Bstroke-linecap%3Around%3B%7D%23mermaid-_r_kj_%20.error-icon%7Bfill%3Argb\(249%2C%20249%2C%20249\)%3B%7D%23mermaid-_r_kj_%20.error-text%7Bfill%3Argb\(13%2C%2013%2C%2013\)%3Bstroke%3Argb\(13%2C%2013%2C%2013\)%3B%7D%23mermaid-_r_kj_%20.edge-thickness-normal%7Bstroke-width%3A1px%3B%7D%23mermaid-_r_kj_%20.edge-thickness-thick%7Bstroke-width%3A3.5px%3B%7D%23mermaid-_r_kj_%20.edge-pattern-solid%7Bstroke-dasharray%3A0%3B%7D%23mermaid-_r_kj_%20.edge-thickness-invisible%7Bstroke-width%3A0%3Bfill%3Anone%3B%7D%23mermaid-_r_kj_%20.edge-pattern-dashed%7Bstroke-dasharray%3A3%3B%7D%23mermaid-_r_kj_%20.edge-pattern-dotted%7Bstroke-dasharray%3A2%3B%7D%23mermaid-_r_kj_%20.marker%7Bfill%3Argb\(93%2C%2093%2C%2093\)%3Bstroke%3Argb\(93%2C%2093%2C%2093\)%3B%7D%23mermaid-_r_kj_%20.marker.cross%7Bstroke%3Argb\(93%2C%2093%2C%2093\)%3B%7D%23mermaid-_r_kj_%20svg%7Bfont-family%3A%22-apple-system%22%2C%22BlinkMacSystemFont%22%2C%22Segoe%20UI%22%2C%22Roboto%22%2C%22Oxygen%22%2C%22Ubuntu%22%2C%22Cantarell%22%2C%22Helvetica%20Neue%22%2C%22Arial%22%2C%22sans-serif%22%3Bfont-size%3A14px%3B%7D%23mermaid-_r_kj_%20p%7Bmargin%3A0%3B%7D%23mermaid-_r_kj_%20.label%7Bfont-family%3A%22-apple-system%22%2C%22BlinkMacSystemFont%22%2C%22Segoe%20UI%22%2C%22Roboto%22%2C%22Oxygen%22%2C%22Ubuntu%22%2C%22Cantarell%22%2C%22Helvetica%20Neue%22%2C%22Arial%22%2C%22sans-serif%22%3Bcolor%3Argb\(13%2C%2013%2C%2013\)%3B%7D%23mermaid-_r_kj_%20.cluster-label%20text%7Bfill%3Argb\(13%2C%2013%2C%2013\)%3B%7D%23mermaid-_r_kj_%20.cluster-label%20span%7Bcolor%3Argb\(13%2C%2013%2C%2013\)%3B%7D%23mermaid-_r_kj_%20.cluster-label%20span%20p%7Bbackground-color%3Atransparent%3B%7D%23mermaid-_r_kj_%20.label%20text%2C%23mermaid-_r_kj_%20span%7Bfill%3Argb\(13%2C%2013%2C%2013\)%3Bcolor%3Argb\(13%2C%2013%2C%2013\)%3B%7D%23mermaid-_r_kj_%20.node%20rect%2C%23mermaid-_r_kj_%20.node%20circle%2C%23mermaid-_r_kj_%20.node%20ellipse%2C%23mermaid-_r_kj_%20.node%20polygon%2C%23mermaid-_r_kj_%20.node%20path%7Bfill%3Argb\(222%2C%20234%2C%20251\)%3Bstroke%3Argb\(83%2C%20154%2C%20248\)%3Bstroke-width%3A1px%3B%7D%23mermaid-_r_kj_%20.rough-node%20.label%20text%2C%23mermaid-_r_kj_%20.node%20.label%20text%2C%23mermaid-_r_kj_%20.image-shape%20.label%2C%23mermaid-_r_kj_%20.icon-shape%20.label%7Btext-anchor%3Amiddle%3B%7D%23mermaid-_r_kj_%20.node%20.katex%20path%7Bfill%3A%23000%3Bstroke%3A%23000%3Bstroke-width%3A1px%3B%7D%23mermaid-_r_kj_%20.rough-node%20.label%2C%23mermaid-_r_kj_%20.node%20.label%2C%23mermaid-_r_kj_%20.image-shape%20.label%2C%23mermaid-_r_kj_%20.icon-shape%20.label%7Btext-align%3Acenter%3B%7D%23mermaid-_r_kj_%20.node.clickable%7Bcursor%3Apointer%3B%7D%23mermaid-_r_kj_%20.root%20.anchor%20path%7Bfill%3Argb\(93%2C%2093%2C%2093\)!important%3Bstroke-width%3A0%3Bstroke%3Argb\(93%2C%2093%2C%2093\)%3B%7D%23mermaid-_r_kj_%20.arrowheadPath%7Bfill%3Argb\(93%2C%2093%2C%2093\)%3B%7D%23mermaid-_r_kj_%20.edgePath%20.path%7Bstroke%3Argb\(93%2C%2093%2C%2093\)%3Bstroke-width%3A2.0px%3B%7D%23mermaid-_r_kj_%20.flowchart-link%7Bstroke%3Argb\(93%2C%2093%2C%2093\)%3Bfill%3Anone%3B%7D%23mermaid-_r_kj_%20.edgeLabel%7Bbackground-color%3Argb\(252%2C%20252%2C%20252\)%3Btext-align%3Acenter%3B%7D%23mermaid-_r_kj_%20.edgeLabel%20p%7Bbackground-color%3Argb\(252%2C%20252%2C%20252\)%3B%7D%23mermaid-_r_kj_%20.edgeLabel%20rect%7Bopacity%3A0.5%3Bbackground-color%3Argb\(252%2C%20252%2C%20252\)%3Bfill%3Argb\(252%2C%20252%2C%20252\)%3B%7D%23mermaid-_r_kj_%20.labelBkg%7Bbackground-color%3Argba\(252%2C%20252%2C%20252%2C%200.5\)%3B%7D%23mermaid-_r_kj_%20.cluster%20rect%7Bfill%3Argb\(249%2C%20249%2C%20249\)%3Bstroke%3Argba\(0%2C%200%2C%200%2C%200.05\)%3Bstroke-width%3A1px%3B%7D%23mermaid-_r_kj_%20.cluster%20text%7Bfill%3Argb\(13%2C%2013%2C%2013\)%3B%7D%23mermaid-_r_kj_%20.cluster%20span%7Bcolor%3Argb\(13%2C%2013%2C%2013\)%3B%7D%23mermaid-_r_kj_%20div.mermaidTooltip%7Bposition%3Aabsolute%3Btext-align%3Acenter%3Bmax-width%3A200px%3Bpadding%3A2px%3Bfont-family%3A%22-apple-system%22%2C%22BlinkMacSystemFont%22%2C%22Segoe%20UI%22%2C%22Roboto%22%2C%22Oxygen%22%2C%22Ubuntu%22%2C%22Cantarell%22%2C%22Helvetica%20Neue%22%2C%22Arial%22%2C%22sans-serif%22%3Bfont-size%3A12px%3Bbackground%3Argb\(249%2C%20249%2C%20249\)%3Bborder%3A1px%20solid%20rgba\(0%2C%200%2C%200%2C%200.05\)%3Bborder-radius%3A2px%3Bpointer-events%3Anone%3Bz-index%3A100%3B%7D%23mermaid-_r_kj_%20.flowchartTitleText%7Btext-anchor%3Amiddle%3Bfont-size%3A18px%3Bfill%3Argb\(13%2C%2013%2C%2013\)%3B%7D%23mermaid-_r_kj_%20rect.text%7Bfill%3Anone%3Bstroke-width%3A0%3B%7D%23mermaid-_r_kj_%20.icon-shape%2C%23mermaid-_r_kj_%20.image-shape%7Bbackground-color%3Argb\(252%2C%20252%2C%20252\)%3Btext-align%3Acenter%3B%7D%23mermaid-_r_kj_%20.icon-shape%20p%2C%23mermaid-_r_kj_%20.image-shape%20p%7Bbackground-color%3Argb\(252%2C%20252%2C%20252\)%3Bpadding%3A2px%3B%7D%23mermaid-_r_kj_%20.icon-shape%20rect%2C%23mermaid-_r_kj_%20.image-shape%20rect%7Bopacity%3A0.5%3Bbackground-color%3Argb\(252%2C%20252%2C%20252\)%3Bfill%3Argb\(252%2C%20252%2C%20252\)%3B%7D%23mermaid-_r_kj_%20.label-icon%7Bdisplay%3Ainline-block%3Bheight%3A1em%3Boverflow%3Avisible%3Bvertical-align%3A-0.125em%3B%7D%23mermaid-_r_kj_%20.node%20.label-icon%20path%7Bfill%3AcurrentColor%3Bstroke%3Arevert%3Bstroke-width%3Arevert%3B%7D%23mermaid-_r_kj_%20.node%20text%7Bfont-size%3A16px%3Bfont-weight%3A600%3Bletter-spacing%3A-0.32px%3Bfill%3A%23004f99%3B%7D%23mermaid-_r_kj_%20.edgeLabels%20text%7Bfont-size%3A13px%3Bfont-weight%3A600%3Bletter-spacing%3A-0.08px%3Bfill%3A%23004f99%3B%7D%23mermaid-_r_kj_%20.node%20tspan%5Bfont-weight%3D%22normal%22%5D%2C%23mermaid-_r_kj_%20.edgeLabels%20tspan%5Bfont-weight%3D%22normal%22%5D%7Bfont-weight%3A600%3B%7D%23mermaid-_r_kj_%20.edgeLabel%20.label%20rect%7Bopacity%3A1%3Brx%3A13px%3Bry%3A13px%3Bfill%3A%23f5faff%3Bstroke%3Argb\(206%2C%20219%2C%20229\)%3Bstroke-width%3A1px%3B%7D%23mermaid-_r_kj_%20.node%20rect%2C%23mermaid-_r_kj_%20.node%20circle%2C%23mermaid-_r_kj_%20.node%20ellipse%2C%23mermaid-_r_kj_%20.node%20polygon%2C%23mermaid-_r_kj_%20.node%20path%7Bfill%3Argb\(229%2C%20243%2C%20255\)%3Bstroke%3Argba\(0%2C%200%2C%200%2C%200.1\)%3Bstroke-width%3A1px%3B%7D%23mermaid-_r_kj_%20.node%20rect%7Brx%3A16px%3Bry%3A16px%3B%7D%23mermaid-_r_kj_%20.node.mermaid-decision%20.label-container%7Bfill%3A%23f5faff%3Bstroke%3Argb\(206%2C%20219%2C%20229\)%3Bstroke-dasharray%3A2%202%3B%7D%23mermaid-_r_kj_%20.edgePaths%20.flowchart-link%7Bstroke%3Argb\(206%2C%20219%2C%20229\)%3Bstroke-width%3A1px%3Bstroke-linecap%3Around%3Bstroke-linejoin%3Around%3B%7D%23mermaid-_r_kj_%20.marker%7Bfill%3Argb\(206%2C%20219%2C%20229\)%3Bstroke%3Argb\(206%2C%20219%2C%20229\)%3B%7D%23mermaid-_r_kj_%20.node%7Bcolor-scheme%3Alight%3B%7D%23mermaid-_r_kj_%20%3Aroot%7B--mermaid-font-family%3A%22-apple-system%22%2C%22BlinkMacSystemFont%22%2C%22Segoe%20UI%22%2C%22Roboto%22%2C%22Oxygen%22%2C%22Ubuntu%22%2C%22Cantarell%22%2C%22Helvetica%20Neue%22%2C%22Arial%22%2C%22sans-serif%22%3B%7D%3C%2Fstyle%3E%3Cg%3E%3Cmarker%20id%3D%22mermaid-_r_kj__flowchart-v2-pointEnd%22%20class%3D%22marker%20flowchart-v2%22%20viewBox%3D%22-5%20-5%2010%2010%22%20refX%3D%220%22%20refY%3D%220%22%20markerUnits%3D%22userSpaceOnUse%22%20markerWidth%3D%2210%22%20markerHeight%3D%2210%22%20orient%3D%22auto%22%3E%3Cpath%20d%3D%22M%200%200%20L%204%200%20M%200.8180194846605362%20-3.181980515339464%20L%204%200%20L%200.8180194846605362%203.181980515339464%22%20class%3D%22arrowMarkerPath%22%20style%3D%22stroke-width%3A%201%3B%20stroke-dasharray%3A%20none%3B%20fill%3A%20none%3B%20stroke-linecap%3A%20round%3B%20stroke-linejoin%3A%20round%3B%22%3E%3C%2Fpath%3E%3C%2Fmarker%3E%3Cmarker%20id%3D%22mermaid-_r_kj__flowchart-v2-pointStart%22%20class%3D%22marker%20flowchart-v2%22%20viewBox%3D%22-5%20-5%2010%2010%22%20refX%3D%220%22%20refY%3D%220%22%20markerUnits%3D%22userSpaceOnUse%22%20markerWidth%3D%2210%22%20markerHeight%3D%2210%22%20orient%3D%22auto%22%3E%3Cpath%20d%3D%22M%200%200%20L%20-4%200%20M%20-0.8180194846605362%20-3.181980515339464%20L%20-4%200%20L%20-0.8180194846605362%203.181980515339464%22%20class%3D%22arrowMarkerPath%22%20style%3D%22stroke-width%3A%201%3B%20stroke-dasharray%3A%20none%3B%20fill%3A%20none%3B%20stroke-linecap%3A%20round%3B%20stroke-linejoin%3A%20round%3B%22%3E%3C%2Fpath%3E%3C%2Fmarker%3E%3Cmarker%20id%3D%22mermaid-_r_kj__flowchart-v2-circleEnd%22%20class%3D%22marker%20flowchart-v2%22%20viewBox%3D%220%200%2010%2010%22%20refX%3D%2211%22%20refY%3D%225%22%20markerUnits%3D%22userSpaceOnUse%22%20markerWidth%3D%2211%22%20markerHeight%3D%2211%22%20orient%3D%22auto%22%3E%3Ccircle%20cx%3D%225%22%20cy%3D%225%22%20r%3D%225%22%20class%3D%22arrowMarkerPath%22%20style%3D%22stroke-width%3A%201%3B%20stroke-dasharray%3A%201%2C%200%3B%22%3E%3C%2Fcircle%3E%3C%2Fmarker%3E%3Cmarker%20id%3D%22mermaid-_r_kj__flowchart-v2-circleStart%22%20class%3D%22marker%20flowchart-v2%22%20viewBox%3D%220%200%2010%2010%22%20refX%3D%22-1%22%20refY%3D%225%22%20markerUnits%3D%22userSpaceOnUse%22%20markerWidth%3D%2211%22%20markerHeight%3D%2211%22%20orient%3D%22auto%22%3E%3Ccircle%20cx%3D%225%22%20cy%3D%225%22%20r%3D%225%22%20class%3D%22arrowMarkerPath%22%20style%3D%22stroke-width%3A%201%3B%20stroke-dasharray%3A%201%2C%200%3B%22%3E%3C%2Fcircle%3E%3C%2Fmarker%3E%3Cmarker%20id%3D%22mermaid-_r_kj__flowchart-v2-crossEnd%22%20class%3D%22marker%20cross%20flowchart-v2%22%20viewBox%3D%220%200%2011%2011%22%20refX%3D%2212%22%20refY%3D%225.2%22%20markerUnits%3D%22userSpaceOnUse%22%20markerWidth%3D%2211%22%20markerHeight%3D%2211%22%20orient%3D%22auto%22%3E%3Cpath%20d%3D%22M%201%2C1%20l%209%2C9%20M%2010%2C1%20l%20-9%2C9%22%20class%3D%22arrowMarkerPath%22%20style%3D%22stroke-width%3A%202%3B%20stroke-dasharray%3A%201%2C%200%3B%22%3E%3C%2Fpath%3E%3C%2Fmarker%3E%3Cmarker%20id%3D%22mermaid-_r_kj__flowchart-v2-crossStart%22%20class%3D%22marker%20cross%20flowchart-v2%22%20viewBox%3D%220%200%2011%2011%22%20refX%3D%22-1%22%20refY%3D%225.2%22%20markerUnits%3D%22userSpaceOnUse%22%20markerWidth%3D%2211%22%20markerHeight%3D%2211%22%20orient%3D%22auto%22%3E%3Cpath%20d%3D%22M%201%2C1%20l%209%2C9%20M%2010%2C1%20l%20-9%2C9%22%20class%3D%22arrowMarkerPath%22%20style%3D%22stroke-width%3A%202%3B%20stroke-dasharray%3A%201%2C%200%3B%22%3E%3C%2Fpath%3E%3C%2Fmarker%3E%3C%2Fg%3E%3Cg%20class%3D%22subgraphs%22%3E%3C%2Fg%3E%3Cg%20class%3D%22nodes%22%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-A-0%22%20transform%3D%22translate\(448.7138214111328%2C%2042\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-113.390625%22%20y%3D%22-30%22%20width%3D%22226.78125%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EConsumer%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20Application%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-B-1%22%20transform%3D%22translate\(448.7138214111328%2C%20142\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-111.75695037841797%22%20y%3D%22-30%22%20width%3D%22223.51390075683594%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EAvaranFlow.Core%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-C-3%22%20transform%3D%22translate\(179.50797271728516%2C%20242\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-125.1417465209961%22%20y%3D%22-30%22%20width%3D%22250.2834930419922%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EAggregation%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20and%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20Context%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-D-5%22%20transform%3D%22translate\(448.7138214111328%2C%20242\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-104.06410217285156%22%20y%3D%22-30%22%20width%3D%22208.12820434570312%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EFan-Out%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20Dispatcher%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-E-7%22%20transform%3D%22translate\(674.6737365722656%2C%20242\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-81.89581298828125%22%20y%3D%22-30%22%20width%3D%22163.7916259765625%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3ESafe%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20Defaults%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-F-9%22%20transform%3D%22translate\(93.03970336914062%2C%20362\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-81.03970336914062%22%20y%3D%22-30%22%20width%3D%22162.07940673828125%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EConsole%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20Sink%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-G-11%22%20transform%3D%22translate\(279.0644226074219%2C%20362\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-64.98501586914062%22%20y%3D%22-30%22%20width%3D%22129.97003173828125%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EFile%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20Sink%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-H-13%22%20transform%3D%22translate\(469.5266418457031%2C%20362\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-85.47720336914062%22%20y%3D%22-30%22%20width%3D%22170.95440673828125%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EDatabase%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20Sink%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-I-15%22%20transform%3D%22translate\(713.1924667358398%2C%20362\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-118.1886215209961%22%20y%3D%22-30%22%20width%3D%22236.3772430419922%22%20height%3D%2260%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-10.5\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EOptional%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20NLog%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20Adapter%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22node%20default%22%20id%3D%22flowchart-J-17%22%20transform%3D%22translate\(713.1924667358398%2C%20467.29999923706055\)%22%3E%3Crect%20class%3D%22basic%20label-container%22%20style%3D%22%22%20x%3D%22-97.4765625%22%20y%3D%22-35.29999923706055%22%20width%3D%22194.953125%22%20height%3D%2270.5999984741211%22%3E%3C%2Frect%3E%3Cg%20class%3D%22label%22%20style%3D%22%22%20transform%3D%22translate\(0%2C%20-19.299999237060547\)%22%3E%3Crect%3E%3C%2Frect%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3Ctext%20y%3D%22-10.1%22%20style%3D%22%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EConsumer's%3C%2Ftspan%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3E%20NLog%3C%2Ftspan%3E%3C%2Ftspan%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%221em%22%20dy%3D%221.1em%22%3E%3Ctspan%20font-style%3D%22normal%22%20class%3D%22text-inner-tspan%22%20font-weight%3D%22normal%22%3EEnvironment%3C%2Ftspan%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edges%20edgePaths%22%3E%3Cpath%20d%3D%22M448.7138214111328%2C72L448.7138214111328%2C100%22%20id%3D%22L_A_B_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_A_B_0%22%20data-points%3D%22W3sieCI6NDQ4LjcxMzgyMTQxMTEzMjgsInkiOjcyfSx7IngiOjQ0OC43MTM4MjE0MTExMzI4LCJ5IjoxMDR9XQ%3D%3D%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3Cpath%20d%3D%22M392.8353462219238%2C172L392.8353462219238%2C185.21704397470535Q392.8353462219238%2C187%20391.74955978429693%2C188.4142135623731L391.74955978429693%2C188.4142135623731Q390.66377334667004%2C189.82842712474618%20389.24955978429693%2C190.91421356237308L389.24955978429693%2C190.9142135623731Q387.8353462219238%2C192%20386.0523901966292%2C192L186.2909287425798%2C192Q184.50797271728516%2C192%20183.09375915491205%2C193.0857864376269L183.09375915491205%2C193.0857864376269Q181.67954559253897%2C194.17157287525382%20180.59375915491205%2C195.5857864376269L180.59375915491205%2C195.5857864376269Q179.50797271728516%2C197%20179.50797271728516%2C198.78295602529465L179.50797271728516%2C202%22%20id%3D%22L_B_C_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_B_C_0%22%20data-points%3D%22W3sieCI6MzkyLjgzNTM0NjIyMTkyMzgsInkiOjE3Mn0seyJ4IjozOTIuODM1MzQ2MjIxOTIzOCwieSI6MTkyfSx7IngiOjE3OS41MDc5NzI3MTcyODUxNiwieSI6MTkyfSx7IngiOjE3OS41MDc5NzI3MTcyODUxNiwieSI6MjA2fV0%3D%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3Cpath%20d%3D%22M448.7138214111328%2C172L448.7138214111328%2C200%22%20id%3D%22L_B_D_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_B_D_0%22%20data-points%3D%22W3sieCI6NDQ4LjcxMzgyMTQxMTEzMjgsInkiOjE3Mn0seyJ4Ijo0NDguNzEzODIxNDExMTMyOCwieSI6MjA0fV0%3D%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3Cpath%20d%3D%22M504.5922966003418%2C172L504.5922966003418%2C185.21704397470535Q504.5922966003418%2C187%20505.6780830379687%2C188.4142135623731L505.6780830379687%2C188.4142135623731Q506.7638694755956%2C189.82842712474618%20508.1780830379687%2C190.91421356237308L508.1780830379687%2C190.9142135623731Q509.5922966003418%2C192%20511.37525262563645%2C192L667.890780546971%2C192Q669.6737365722656%2C192%20671.0879501346387%2C193.0857864376269L671.0879501346387%2C193.08578643762692Q672.5021636970118%2C194.17157287525382%20673.5879501346387%2C195.5857864376269L673.5879501346387%2C195.5857864376269Q674.6737365722656%2C197%20674.6737365722656%2C198.78295602529465L674.6737365722656%2C202%22%20id%3D%22L_B_E_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_B_E_0%22%20data-points%3D%22W3sieCI6NTA0LjU5MjI5NjYwMDM0MTgsInkiOjE3Mn0seyJ4Ijo1MDQuNTkyMjk2NjAwMzQxOCwieSI6MTkyfSx7IngiOjY3NC42NzM3MzY1NzIyNjU2LCJ5IjoxOTJ9LHsieCI6Njc0LjY3MzczNjU3MjI2NTYsInkiOjIwNn1d%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3Cpath%20d%3D%22M386.2753601074219%2C272L386.2753601074219%2C285.21704397470535Q386.2753601074219%2C287%20385.189573669795%2C288.4142135623731L385.189573669795%2C288.4142135623731Q384.1037872321681%2C289.8284271247462%20382.689573669795%2C290.9142135623731L382.689573669795%2C290.9142135623731Q381.2753601074219%2C292%20379.4924040821272%2C292L99.82265939443528%2C292Q98.03970336914062%2C292%2096.62548980676753%2C293.0857864376269L96.62548980676753%2C293.0857864376269Q95.21127624439444%2C294.1715728752538%2094.12548980676755%2C295.5857864376269L94.12548980676753%2C295.5857864376269Q93.03970336914062%2C297%2093.03970336914062%2C298.78295602529465L93.03970336914062%2C320%22%20id%3D%22L_D_F_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_D_F_0%22%20data-points%3D%22W3sieCI6Mzg2LjI3NTM2MDEwNzQyMTksInkiOjI3Mn0seyJ4IjozODYuMjc1MzYwMTA3NDIxOSwieSI6MjkyfSx7IngiOjkzLjAzOTcwMzM2OTE0MDYyLCJ5IjoyOTJ9LHsieCI6OTMuMDM5NzAzMzY5MTQwNjIsInkiOjMyNH1d%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3Cpath%20d%3D%22M427.9010009765625%2C272L427.9010009765625%2C305.21704397470535Q427.9010009765625%2C307%20426.8152145389356%2C308.4142135623731L426.8152145389356%2C308.4142135623731Q425.7294281013087%2C309.8284271247462%20424.3152145389356%2C310.9142135623731L424.3152145389356%2C310.9142135623731Q422.9010009765625%2C312%20421.11804495126785%2C312L285.8473786327165%2C312Q284.0644226074219%2C312%20282.65020904504877%2C313.0857864376269L282.65020904504877%2C313.0857864376269Q281.23599548267566%2C314.1715728752538%20280.15020904504877%2C315.5857864376269L280.15020904504877%2C315.5857864376269Q279.0644226074219%2C317%20279.0644226074219%2C318.78295602529465L279.0644226074219%2C322%22%20id%3D%22L_D_G_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_D_G_0%22%20data-points%3D%22W3sieCI6NDI3LjkwMTAwMDk3NjU2MjUsInkiOjI3Mn0seyJ4Ijo0MjcuOTAxMDAwOTc2NTYyNSwieSI6MzEyfSx7IngiOjI3OS4wNjQ0MjI2MDc0MjE5LCJ5IjozMTJ9LHsieCI6Mjc5LjA2NDQyMjYwNzQyMTksInkiOjMyNn1d%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3Cpath%20d%3D%22M469.5266418457031%2C272L469.5266418457031%2C320%22%20id%3D%22L_D_H_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_D_H_0%22%20data-points%3D%22W3sieCI6NDY5LjUyNjY0MTg0NTcwMzEsInkiOjI3Mn0seyJ4Ijo0NjkuNTI2NjQxODQ1NzAzMSwieSI6MzI0fV0%3D%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3Cpath%20d%3D%22M511.1522827148438%2C272L511.15228271484375%2C284.9289321881345Q511.15228271484375%2C292%20518.2233505267092%2C292L706.4095107105452%2C292Q708.1924667358398%2C292%20709.606680298213%2C293.0857864376269L709.606680298213%2C293.0857864376269Q711.020893860586%2C294.1715728752538%20712.106680298213%2C295.5857864376269L712.106680298213%2C295.5857864376269Q713.1924667358398%2C297%20713.1924667358398%2C298.78295602529465L713.1924667358398%2C320%22%20id%3D%22L_D_I_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_D_I_0%22%20data-points%3D%22W3sieCI6NTExLjE1MjI4MjcxNDg0MzgsInkiOjI3Mn0seyJ4Ijo1MTEuMTUyMjgyNzE0ODQzNzUsInkiOjI5Mn0seyJ4Ijo3MTMuMTkyNDY2NzM1ODM5OCwieSI6MjkyfSx7IngiOjcxMy4xOTI0NjY3MzU4Mzk4LCJ5IjozMjR9XQ%3D%3D%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3Cpath%20d%3D%22M713.1924667358398%2C392L713.1924667358398%2C420%22%20id%3D%22L_I_J_0%22%20class%3D%22edge-thickness-normal%20edge-pattern-solid%20edge-thickness-normal%20edge-pattern-solid%20flowchart-link%22%20style%3D%22%3B%22%20data-edge%3D%22true%22%20data-et%3D%22edge%22%20data-id%3D%22L_I_J_0%22%20data-points%3D%22W3sieCI6NzEzLjE5MjQ2NjczNTgzOTgsInkiOjM5Mn0seyJ4Ijo3MTMuMTkyNDY2NzM1ODM5OCwieSI6NDI0fV0%3D%22%20marker-end%3D%22url\(%23mermaid-_r_kj__flowchart-v2-pointEnd\)%22%3E%3C%2Fpath%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabels%22%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%3E%3Crect%20class%3D%22background%22%20style%3D%22stroke%3A%20none%22%3E%3C%2Frect%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_A_B_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_B_C_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_B_D_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_B_E_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_D_F_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_D_G_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_D_H_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_D_I_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3Cg%20class%3D%22edgeLabel%22%3E%3Cg%20class%3D%22label%22%20data-id%3D%22L_I_J_0%22%20transform%3D%22translate\(0%2C%200\)%22%3E%3Ctext%20y%3D%22-10.1%22%3E%3Ctspan%20class%3D%22text-outer-tspan%22%20x%3D%220%22%20y%3D%22-0.1em%22%20dy%3D%221.1em%22%3E%3C%2Ftspan%3E%3C%2Ftext%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fg%3E%3C%2Fsvg%3E)

The packages should have the following responsibilities:

| Package                          | Responsibility                                                 | NLog dependency                                          |
| -------------------------------- | -------------------------------------------------------------- | -------------------------------------------------------- |
| `AvaranFlow.Core`           | Public API, context, aggregation, dispatch, safe configuration | None                                                     |
| `AvaranFlow.Sinks.Console`  | Console output                                                 | None                                                     |
| `AvaranFlow.Sinks.File`     | File writing, rotation and recovery                            | None                                                     |
| `AvaranFlow.Sinks.Database` | Configurable SQL persistence                                   | None unless a provider is chosen that requires a package |
| `AvaranFlow.NLog`     | Adapt ExceptionMaster events to NLog                           | Yes, isolated to this package                            |
| `ExceptionMaster.Tests`          | Core, sink, integration, and failure tests                     | Test dependencies only as required                       |

These are proposed package boundaries, not a requirement to publish five packages immediately. The codebase can initially be a single solution with separate projects and later publish the packages independently.

### NLog-specific rules

The NLog adapter must:

* Avoid exposing NLog types in the public core API.

* Avoid installing a default `NLog.config` into the consumer's application.

* Avoid replacing the consumer's global NLog configuration.

* Avoid calling `LogManager.Shutdown()` or otherwise shutting down a logger owned by the host.

* Avoid assuming that a particular NLog version is already installed.

* Document its supported NLog versions and target frameworks.

* Allow the consuming application to use its existing NLog configuration when the adapter is compatible with it.

* Fail safely if NLog is unavailable, misconfigured, or unable to write.

Dependency decision: An optional NLog adapter prevents the core package from introducing an NLog dependency. It does not magically eliminate version conflicts if the consumer installs an incompatible NLog adapter alongside another NLog version. That must be handled through explicit package compatibility, dependency resolution, and integration testing.

Do not use reflection-based late binding merely to claim conflict-free support unless there is a demonstrated requirement and a tested design for it.

## 5. Public API and consumer customization

The consumer should be able to use ExceptionMaster with minimal configuration or fully customize its behavior.

### Expected usage patterns

C#

```
using AvaranFlow.Core;

// Simplest usage: use safe default configuration.
ExceptionMaster.Log(ex);
```

C#

```
// Custom category, correlation ID and fatal flag.
ExceptionMaster.Log(
    ex,
    category: "Database",
    correlationId: requestId,
    isFatal: false);
```

C#

```
// Explicitly configure sinks.
ExceptionMaster.Configure(options =>
{
    options.EnableAggregation = true;
    options.BufferSize = 100;
    options.FlushInterval = TimeSpan.FromSeconds(5);

    // Register only the destinations this application needs.
    // Configure file, SQL, console and/or custom sinks here.
});
```

The examples describe the intended API, not a claim that the implementation already exists.

### Configuration requirements

The API should provide:

* `Configure(...)` for custom configuration.

* `ConfigureDefaults()` to restore or establish documented defaults.

* A default initialization path when the user calls `Log` before configuring the library.

* Explicit opt-in or opt-out for each built-in sink.

* User-supplied connection settings for SQL.

* User-supplied file path and retention settings for file output.

* Configurable aggregation, buffer capacity, flush interval, queue capacity, and overflow behavior.

* A public `IExceptionSink` contract for custom destinations.

* A documented configuration replacement/reconfiguration policy.

* A deterministic way to flush pending events and shut down the library during normal application shutdown.

The default path should not require a database, NLog, or an external configuration file.

### Configuration precedence

The implementation must define what happens when:

1. No configuration exists.

2. Defaults are initialized automatically.

3. A consumer provides custom options.

4. The consumer calls `Configure` more than once.

5. One sink is invalid while others are valid.

6. Configuration changes while logging is occurring.

Do not allow a reconfiguration to dispose of sinks that are still being used by in-flight operations. Publish immutable configuration snapshots or use another explicitly thread-safe lifecycle strategy.

## 6. SQL configuration and optional database logging

Database logging is optional. A consumer must not be forced to configure SQL simply because the package supports it.

### Required behavior

* No connection string means database logging is disabled unless a connection is provided through a documented external mechanism.

* A configured but invalid connection string must not crash the host.

* A database outage must trigger the applicable fallback behavior.

* Database connection recovery must be possible without restarting the host where practical.

* Database operations must support timeouts.

* Database writes must be parameterized; never concatenate exception messages into SQL commands.

* The database provider and supported database engines must be explicitly documented.

* Credentials must not be written into log messages, configuration diagnostics, or exception context.

* The schema must support exception identity, aggregation, first and last occurrence, and diagnostic metadata.

* SQL writes should be batched where supported, with a bounded buffer.

* Database failures must not cause an unbounded retry loop or unbounded in-memory growth.

### SQL schema fields

At minimum, a database record should support:

* Stable exception or aggregation key.

* Exception type.

* Exception message.

* Full exception details and stack trace.

* Caller and category.

* First-seen UTC and last-seen UTC.

* Occurrence count.

* Machine name and process name.

* Thread ID.

* Application version.

* Correlation ID.

* Fatal flag.

* Record creation and last-update timestamps.

Additional fields and their rationale are covered in Section 9.

The agent must produce the SQL schema and any required indexes, and verify that the chosen aggregation strategy is safe under concurrent writers. In-memory deduplication alone does not guarantee a single aggregate row across multiple processes or application instances.

## 7. File and console sinks

### File sink

The file sink must support:

* A configurable directory and file name.

* A documented default location.

* Directory creation when permitted.

* Size-based rotation and bounded archive retention.

* Concurrent calls from multiple threads.

* Appropriate file sharing behavior.

* Handling of disk-full, permission-denied, locked-file, unavailable-path, and I/O errors.

* Safe disposal and flush during normal shutdown.

* Bounded buffering and documented data-loss behavior during process termination.

* No recursive use of ExceptionMaster to report the file sink's own failure.

File logging should be usable without NLog.

### Console sink

The console sink must:

* Work without NLog.

* Be independently configurable.

* Handle the absence of an attached console or unavailable output safely.

* Avoid allowing a write failure to propagate.

* Provide a useful minimal emergency diagnostic when other destinations fail, where the runtime permits it.

A library cannot guarantee that console output is physically retained or even available in every host, such as a Windows GUI application running without a console. That is a limitation to document and test rather than conceal.

## 8. Fallback versus fan-out — two distinct behaviors

These concepts were discussed together but must remain separate in the design.

### Fallback

Try the next destination when the current destination cannot accept the event.

SQL

If unsuccessful

File

If unsuccessful

Console

### Fan-out

Attempt delivery to every independently configured sink, regardless of whether another sink succeeds.

One exception event

SQL

File

Console

The consumer must be able to select the behavior rather than having it hard-coded.

For example, a consumer might require SQL and file output simultaneously, while another might prefer SQL with file and console used only as fallbacks.

### Delivery guarantees

A sink method returning successfully is not necessarily proof that the event was durably committed. For example, an asynchronous sink might only have accepted the event into its queue.

The implementation must define whether fallback is triggered by:

* A synchronous sink exception.

* A failed enqueue.

* An asynchronous write failure.

* A database command failure.

* A confirmed persistence failure.

If asynchronous failures are detected after the original fan-out call returns, fallback must be implemented by the dispatcher or sink pipeline, not assumed to happen automatically.

A single destination should not accidentally receive the same event twice because both fan-out and fallback retry it. Retries and fallback attempts need event identity and documented duplicate-delivery semantics.

## 9. Exception context — all requested fields

The context is the framework-independent contract shared by all sinks and event subscribers.

It must be immutable to consumers, or expose a snapshot whose values cannot be mutated after publication.

| Field                   | Type / representation        | Purpose                                                            |
| ----------------------- | ---------------------------- | ------------------------------------------------------------------ |
| `Exception`             | `Exception`                  | Original exception object                                          |
| `Message`               | `string`                     | Exception message                                                  |
| `ExceptionType`         | `string`                     | Fully qualified exception type                                     |
| `StackTrace`            | `string`                     | Full exception details and stack trace                             |
| `FirstSeenUtc`          | `DateTime` UTC               | First occurrence in the aggregation window                         |
| `LastSeenUtc`           | `DateTime` UTC               | Most recent occurrence                                             |
| `Count`                 | `long` preferred             | Total occurrences represented by the aggregate                     |
| `Caller`                | `string`                     | Calling method/member                                              |
| `Category`              | `string`                     | Logical grouping, such as Database, Network, IO, or UI             |
| `MachineName`           | `string`                     | Host identity                                                      |
| `ProcessName`           | `string`                     | Application or worker process                                      |
| `ThreadId`              | `int`                        | Managed thread ID at capture time                                  |
| `AppVersion`            | `string`                     | Application version                                                |
| `CorrelationId`         | `string`                     | Request, order, operation, or workflow trace                       |
| `IsFatal`               | `bool`                       | Caller-supplied indication that the event is fatal                 |
| `ExceptionKey`          | `string`                     | Stable identity used for aggregation                               |
| `ProcessId`             | `int`                        | Distinguishes processes on the same machine                        |
| `EnvironmentName`       | `string`                     | Optional environment such as Production or Test                    |
| `Source`                | `string`                     | Optional subsystem or component name                               |
| `InnerExceptionDetails` | Structured or formatted text | Preserves nested exception information                             |
| `OccurrenceId`          | `string` or GUID             | Identifies an individual captured occurrence if needed for tracing |
| `SchemaVersion`         | `int`                        | Allows stored record formats to evolve                             |

The last fields are additional recommendations, not all original mandatory fields. The agent should justify and document any further fields it adds.

### Metadata collection rules

* `Caller` should default to the caller member name through `[CallerMemberName]` where possible.

* A caller-supplied category should be supported.

* `CorrelationId` should be optional and supplied by the application or its tracing context.

* `IsFatal` should be explicitly provided by the caller or a documented policy. Do not infer that every `OutOfMemoryException`, database exception, or unhandled exception necessarily means the application is terminating.

* `MachineName`, `ProcessName`, `ProcessId`, `ThreadId`, and application version should be captured safely.

* Environment and version information should not be repeatedly recomputed unnecessarily on the hot path.

* A missing value should be represented consistently, not by arbitrary strings in some fields and nulls in others.

* Full stack traces must be retained even when the aggregation key uses only a small portion of the trace.

* PDB files may be needed for source file and line information. The library must not promise line numbers when the required debugging symbols are unavailable.

### Important aggregation nuance

`FirstSeenUtc`, `LastSeenUtc`, and `Count` describe an aggregate, not an immutable individual occurrence.

The implementation must therefore define how those fields are snapshotted and delivered to sinks. If the first occurrence is queued and the same exception repeats before the queue is flushed, the framework must not silently emit contradictory snapshots or mutate a context object that a sink is already processing.

Use immutable snapshots or another well-defined synchronization strategy.

## 10. Aggregation and deduplication

The original rule proposed was:

`ExceptionType + Message + TopStackFrame`

This is a starting point, not a universally reliable identity. Different failures can share the same message and first stack frame; conversely, a changing stack can split one logical issue into many keys.

### Required behavior

1. First occurrence creates an aggregate and is eligible for immediate logging.

2. Repeated matching occurrences update the count and last-seen timestamp.

3. Repeated occurrences must not automatically create a new persistent record each time.

4. The consumer can configure periodic summaries or re-logging.

5. The first and last timestamps must remain correct under concurrency.

6. Aggregation must be bounded or have a defined eviction/expiry policy.

7. The system must prevent counter overflow from corrupting state.

8. The key-generation policy must be documented and testable.

9. The library must support resetting or expiring aggregation windows without losing count information unexpectedly.

### What must be corrected from earlier snippets

The earlier example used a mutable `ExceptionStats` object inside a `ConcurrentDictionary`. A concurrent dictionary does not make the object's mutable properties thread-safe. A `private set` property and `Interlocked.Increment` on one field do not make a multi-field snapshot atomic.

The earlier example also used a `Count % 100 == 0` condition. That is merely a periodic threshold example, not a complete configurable aggregation policy.

The final implementation should use an explicit concurrency model: for example, per-entry synchronization, immutable replacement, or a carefully designed atomic state update. It must test the resulting snapshots under parallel load.

### Count semantics

Document whether `Count` represents:

* Total exceptions observed in the current aggregate, including the first occurrence.

* Total occurrences since process start.

* Total occurrences in a time window.

* Total occurrences represented in the current persisted row.

These are not necessarily the same number. If the library restarts, an in-memory counter is lost unless aggregate state is recovered from persistent storage.

## 11. Buffering, async dispatch and flush intervals

The original requirement explicitly called for a buffer and a configurable dump/flush interval.

This must be implemented independently of whether the NLog adapter is installed.

### Configuration options

At minimum, the options should cover:

* Buffer capacity.

* Flush interval.

* Batch size.

* Maximum queue capacity.

* Overflow policy.

* Shutdown flush timeout.

* Sink-specific batch settings where applicable.

* Whether critical/fatal events are allowed a special flush policy.

A default configuration might start with a buffer capacity of 100 and a five-second flush interval, as discussed earlier. These are initial defaults to benchmark, not universally optimal settings.

### Queue overflow

A bounded queue is necessary to avoid unbounded memory growth during a logging storm.

The policy must be explicit:

* Drop newest.

* Drop oldest.

* Block the producer for a bounded time.

* Apply a special policy for fatal events, if feasible.

Blocking must not be introduced silently into latency-sensitive calling threads. Conversely, asynchronous dispatch with a discard policy can lose exception events. The trade-off must be documented and measurable.

### Flush behavior

* Flush after the configured interval.

* Flush when the batch threshold is reached.

* Attempt a bounded flush during orderly shutdown.

* Do not block indefinitely on an unavailable SQL server.

* Do not claim that buffered events survive abrupt process termination.

* Expose diagnostics or counters for dropped events, failed writes, retry attempts, and pending queue depth.

* Ensure that the flush timer, worker, and queue shut down without deadlocks.

The library should not flush on every event by default, as that can undermine throughput.


## 12. Fire-and-forget event and recursion protection

The event requirement is broader than simply invoking a C# event on a thread-pool thread.

The intended contract is:

C#

```
public static event Action<ExceptionContext> ExceptionRaised;
```

Consumers can subscribe to the event to trigger additional actions, such as metrics, alerts, or a separate diagnostics pipeline.

### Required behavior

* Event notification must not block the logging caller.

* Each subscriber's failure must be isolated from the logger and from other subscribers.

* Subscriber exceptions must not be reported by recursively publishing them to the same event without a guard.

* A subscriber may call `ExceptionMaster.Log(...)` itself without causing an infinite chain.

* The event should be configurable or disableable.

* Subscribers should be invoked asynchronously through a bounded dispatch mechanism.

* Event exceptions and dropped notifications should be observable through non-recursive internal diagnostics where possible.

* The event should not prevent configured sinks from receiving the original exception.

* Subscription and unsubscription must be thread-safe.

* The framework must not keep unnecessary strong references to subscribers indefinitely.

* Normal shutdown must define how queued notifications are handled.

### Why the earlier global guard is insufficient

The previous example used a single static integer guard around the event. This prevents some recursive cases, but it also suppresses unrelated exceptions logged concurrently while the guard is set. It is not a correct general-purpose reentrancy strategy.

A stronger design should distinguish event-originated logging from ordinary application logging. It can use a per-dispatch execution context or explicit internal origin metadata to prevent recursive event publication while allowing unrelated exceptions to be processed normally.

The guard must be exception-safe, must be released on every exit path, and must behave correctly under concurrent event delivery.

### Example

1. Application logs exception A.

2. The exception is dispatched to configured sinks.

3. An asynchronous notification is queued for A.

4. A subscriber processes A and encounters exception B.

5. The subscriber calls `ExceptionMaster.Log(B)`.

6. B is logged to configured sinks.

7. B does not generate an uncontrolled notification chain back into the same event subscriber.

8. The original logging caller is unaffected by either exception.

This is the required behavior; the exact implementation mechanism should be selected and verified by tests.

## 13. Failure isolation and fallback matrix

The framework must be designed for partial failure, not only normal operation.

| Failure                                 | Required response                                                                                |
| --------------------------------------- | ------------------------------------------------------------------------------------------------ |
| SQL server unavailable                  | Attempt the configured file/console fallback; do not propagate the failure                       |
| Invalid SQL connection string           | Disable or mark the database sink unhealthy according to policy; retain other sinks              |
| SQL timeout                             | Enforce a timeout and avoid unbounded retrying                                                   |
| File is locked                          | Apply the configured sharing/retry policy; continue to other sinks                               |
| Disk full                               | Stop futile file retries according to policy; try other available destinations                   |
| File permission denied                  | Treat the file sink as unavailable and continue                                                  |
| Directory unavailable                   | Attempt recovery where reasonable; otherwise continue with other sinks                           |
| Console unavailable                     | Swallow the sink failure                                                                         |
| Custom sink throws                      | Isolate that sink; continue fan-out                                                              |
| NLog target fails                       | Prevent the adapter failure from escaping; use configured fallback behavior                      |
| Buffer full                             | Apply the selected overflow policy                                                               |
| Event subscriber throws                 | Isolate the subscriber                                                                           |
| Event subscriber logs another exception | Prevent uncontrolled recursive notifications                                                     |
| Missing configuration                   | Initialize safe defaults                                                                         |
| Partial configuration                   | Retain valid sinks and report invalid configuration through a non-recursive diagnostic mechanism |
| Logging during shutdown                 | Make a bounded best effort; never wait indefinitely                                              |
| Logging during AppDomain unload         | Avoid unsafe background work and tolerate unavailable runtime resources                          |
| Aggregation store pressure              | Apply a documented eviction or expiry policy                                                     |
| Extreme exception storm                 | Bound memory, queues, retries, and diagnostic output                                             |

### Core rule

No sink, event subscriber, retry worker, timer, or internal diagnostic component should report its own failures by recursively calling the same failing path.

Use a separate emergency reporting mechanism with tightly controlled behavior, such as a minimal best-effort console write, where available. It must not become another source of recursive logging.

## 14. NuGet packaging and repository structure

The project must be a real, buildable Visual Studio solution, not just a collection of code snippets.

Recommended repository structure:

```
ExceptionMaster/
│
├── AvaranFlow.sln
│
├── src/
│   ├── AvaranFlow.Core/
│   ├── AvaranFlow.Sinks.Console/
│   ├── AvaranFlow.Sinks.File/
│   ├── AvaranFlow.Sinks.Database/
│   └── AvaranFlow.NLog/
│
├── tests/
│   ├── AvaranFlow.Core.Tests/
│   ├── AvaranFlow.Sinks.Tests/
│   ├── AvaranFlow.Concurrency.Tests/
│   ├── AvaranFlow.FailureInjection.Tests/
│   └── AvaranFlow.Integration.Tests/
│
├── samples/
│   ├── ExceptionMaster.Sample.ClassicConsole/
│   ├── ExceptionMaster.Sample.WinForms/
│   └── ExceptionMaster.Sample.CustomSinks/
│
├── docs/
│   ├── ARCHITECTURE.md
│   ├── API.md
│   ├── CONFIGURATION.md
│   ├── FALLBACK_AND_FANOUT.md
│   ├── DATABASE_SCHEMA.md
│   ├── PERFORMANCE.md
│   └── TEST_PLAN.md
│
├── Directory.Build.props
├── README.md
├── CHANGELOG.md
├── LICENSE
└── .gitignore
```

The agent may adjust the directory layout to suit the actual project tooling, but must preserve the separation of core logic, optional integrations, tests, samples, and documentation.

### Packaging requirements

Each package must have:

* Explicit package ID and version.

* Correct target-framework declarations.

* Minimal, intentional dependencies.

* Package description and README.

* XML documentation for public APIs.

* Release notes or changelog.

* A reproducible build and pack process.

* A sample consumer demonstrating installation and use.

* No accidental inclusion of development secrets, local connection strings, or machine-specific paths.

The solution should produce `.nupkg` artifacts locally and validate their contents before any publication.

Do not publish packages to a public NuGet feed without explicit authorization.

## 15. Unit tests and validation strategy

The original request was for tests covering all possible actions. Literally testing every possible environmental failure is impossible, so the project must define a broad, systematic test matrix with deterministic failure injection and integration tests for actual infrastructure.

### A. Core API and defaults

* Calling `Log(null)` does not throw.

* Calling `Log(exception)` before configuration does not throw.

* Missing NLog does not prevent core operation.

* Missing SQL configuration does not prevent logging to other destinations.

* `ConfigureDefaults()` establishes the documented default behavior.

* Invalid or partially valid configuration does not break logging.

* Reconfiguration while other threads are logging is safe.

* Custom sinks can be registered and invoked.

### B. Context and metadata

* Original exception is preserved.

* Message and exception type are correct.

* Full stack and inner-exception information are retained.

* First-seen and last-seen timestamps are UTC and accurate.

* Count includes the first occurrence.

* Caller, category, and correlation ID are captured correctly.

* Machine, process, thread, and application version metadata are populated safely.

* `IsFatal` reflects the caller-supplied value.

* Missing optional metadata is handled consistently.

* Context snapshots cannot be mutated unexpectedly after publication.

### C. Aggregation

* First occurrence creates an aggregate.

* Identical logical exceptions update the existing count.

* Different exception types do not collide.

* Different messages or configured identity frames behave according to the key policy.

* First-seen time remains unchanged within the aggregate.

* Last-seen time advances correctly.

* Periodic re-logging follows the configured policy.

* Aggregation expiry and eviction work correctly.

* Large counts do not wrap silently.

* Parallel calls produce correct counts and coherent snapshots.

* Multiple application instances do not create duplicate persisted aggregates when the selected SQL strategy promises cross-process aggregation.

### D. Fan-out and fallback

* Every enabled fan-out sink is attempted.

* One sink throwing does not prevent other sinks from receiving the event.

* Disabled sinks are not called.

* SQL failure triggers the configured fallback policy.

* File failure triggers the configured fallback policy.

* SQL failure does not disable an otherwise healthy file sink.

* A sink recovery restores delivery according to policy.

* Fallback does not create unintended duplicate deliveries.

* Asynchronous sink failures are handled by the correct pipeline.

* No sink failure escapes the public logging API.

### E. Fire-and-forget event

* Event subscribers receive the intended context.

* A slow subscriber does not block the caller.

* A throwing subscriber does not prevent other subscribers from running.

* A subscriber can log another exception without an infinite event cycle.

* Unrelated concurrent exceptions are not incorrectly suppressed by a global guard.

* Unsubscription works.

* Notification queue saturation follows the documented overflow policy.

* Shutdown handles queued events within the configured time budget.

### F. Database sink

* Valid connection and successful insertion.

* No connection string configured.

* Invalid connection string.

* Database unavailable at startup.

* Database disconnects after successful writes.

* Database timeout.

* SQL command failure.

* Schema mismatch.

* Authentication failure.

* Parameterized insertion of messages containing quotes, Unicode, and multiline stack traces.

* Batch failure and partial batch recovery.

* Retry limits and recovery behavior.

* Credentials do not leak into diagnostics.

* Aggregation updates are correct under concurrent writers.

### G. File sink

* Initial file creation.

* Directory creation.

* Repeated writes.

* Concurrent writes.

* File rotation.

* Archive retention.

* File locked by another process.

* Permission denied.

* Disk full, simulated where practical.

* Invalid path.

* Directory removed while running.

* File recovery after the fault clears.

* Shutdown flush.

* Bounded memory during sustained failure.

### H. Console and NLog integration

* Console output works when available.

* Console write failure is isolated.

* No console is attached.

* NLog adapter is not installed.

* Compatible existing NLog installation.

* Supported NLog versions are verified.

* Consumer's NLog configuration remains unchanged unless explicit configuration is requested.

* NLog target failure is contained.

* NLog shutdown remains under host ownership.

* Adapter does not introduce a mandatory NLog dependency into the core package.

### I. Concurrency, stress, and performance

* Concurrent logging from many threads.

* Concurrent logging and reconfiguration.

* Concurrent logging and shutdown.

* High-volume repeated identical exceptions.

* High-volume unique exceptions.

* Slow and failed sinks.

* Queue saturation.

* Memory behavior under long-running load.

* No deadlocks or unbounded worker creation.

* No unexpected suppression of unrelated exceptions.

* Benchmark overhead for capture, aggregation, queueing, and each sink.

* Measure throughput, latency percentiles, memory consumption, dropped events, and flush duration.

A million-event stress test can be included as a benchmark scenario, but it must not be treated as a universal performance guarantee or as a replacement for correctness assertions.

### Test infrastructure

Use a framework compatible with the chosen target, such as MSTest or NUnit, and keep unit tests independent of a live SQL server wherever possible.

Use:

* Fake sinks.

* Throwing sinks.

* In-memory sinks.

* Controllable clocks for timestamp tests.

* Fault-injection abstractions.

* A disposable test database for integration tests.

* Isolated temporary directories for file tests.

* Repeated and parallel test runs for race-condition detection.

Tests that require actual operating-system failures, disk exhaustion, or process termination should be isolated and simulated where safe; do not make a developer workstation unstable to test these cases.


## 16. Performance requirements for latency-sensitive applications

The library is intended to be reused in applications where logging overhead matters, including trading software, market-data processing, APIs, and background services.

The architecture must therefore avoid making slow I/O part of the normal caller's critical path unless the consumer explicitly chooses synchronous behavior.

### Required performance principles

* Keep the public `Log` method short and failure-contained.

* Avoid synchronous SQL or file writes on the producer thread when asynchronous dispatch is enabled.

* Avoid repeatedly capturing stack traces merely to identify a caller.

* Cache stable environment metadata where appropriate.

* Avoid unbounded task creation, queues, retry loops, or background threads.

* Avoid a global lock that serializes all logging.

* Avoid expensive string formatting until a sink needs the formatted representation, where practical.

* Avoid unnecessary copies of large exception stacks.

* Bound aggregation state and define eviction behavior.

* Make performance settings configurable and benchmark them.

* Ensure the failure path is also bounded; an unavailable database must not cause every logging call to wait for a network timeout.

### No unsupported performance claims

The earlier proposal stated that the design would be safe for high-frequency trading use and that it would have no locks or allocations on the hot path. Those claims cannot be guaranteed by architecture alone.

A correct implementation may need synchronization, immutable snapshots, or allocations to preserve safety. The objective is to minimize and measure those costs without sacrificing correctness.

Antigravity must provide actual benchmark results from the implemented code, with the test environment, event distribution, enabled sinks, concurrency, and configuration documented.

## 17. Lifecycle management and application shutdown

The library must have a defined lifecycle because buffering and background dispatch introduce state that must be managed.

The design should support:

* Initialization on first use.

* Explicit configuration before or after first use, according to documented rules.

* Safe operation while configuration is being replaced.

* Explicit flush.

* Bounded shutdown.

* Disposal of library-owned resources.

* Separation of library-owned and host-owned resources.

* Defined behavior for pending events during shutdown.

* Prevention of new background work after shutdown has begun, except where explicitly supported.

* Safe handling of repeated shutdown calls.

For example, the library must never shut down the consumer's global NLog instance merely because its own adapter is being disposed.

Normal shutdown should make a best effort to flush pending logs, while process crashes and abrupt termination must be treated as possible data-loss scenarios.

The logging API must also tolerate being called during AppDomain unload or when some runtime resources are no longer available. It cannot guarantee persistence in such conditions.

## 18. Security and operational safeguards

These were not separate original feature requests, but they follow directly from the requirement to make the library reusable in production.

* Parameterize SQL commands.

* Do not log database passwords, connection strings, authentication tokens, or other secrets.

* Provide a policy for masking sensitive values in exception messages and stack traces.

* Do not make claims that all exception data is safe to persist.

* Restrict file locations and use appropriate operating-system permissions.

* Limit archive retention and buffer growth.

* Avoid creating log files in unexpected directories.

* Document that caller names, machine names, paths, and exception text can contain sensitive information.

* Ensure internal diagnostics cannot recurse into the same failing pipeline.

* Provide counters for sink health, dropped events, retries, and queue pressure without creating another unbounded logging channel.

The library should not silently discard every diagnostic about its own failures. Instead, it should use a separate, minimal, non-recursive emergency path and expose health counters where practical.

## 19. Configuration examples and expected consumer experience

The finished project must include documentation and working samples for these scenarios.

### Scenario A — Zero configuration

A developer references `AvaranFlow.Core` and calls `Log`. The logger initializes safe defaults, requires no NLog configuration, and does not throw if output is unavailable.

### Scenario B — File only

The consumer configures the file sink and disables SQL and console output. File failures are contained.

### Scenario C — SQL with fallback

The consumer provides a database connection and configures file and console as fallbacks. SQL failure triggers the documented fallback path.

### Scenario D — Multi-sink fan-out

The consumer sends the same exception to SQL, file, and console independently. Failure in one does not prevent attempts to the others.

### Scenario E — Existing NLog application

The consumer installs the optional NLog adapter, selects a compatible version, and retains ownership of the host's existing NLog configuration.

### Scenario F — Custom sink

The consumer implements `IExceptionSink` for a private diagnostics service or another destination without modifying core code.

### Scenario G — Repeated exceptions

One hundred matching exceptions produce an aggregate count of 100, with a stable first-seen time and an updated last-seen time, subject to the configured aggregation window.

The examples must be executable samples, not merely illustrative code fragments.

## 20. Documentation deliverables

The repository must include the following documentation.

| File                     | Required content                                                                 |
| ------------------------ | -------------------------------------------------------------------------------- |
| `README.md`              | Purpose, supported frameworks, quick start, installation, configuration, samples |
| `ARCHITECTURE.md`        | Canonical design and component responsibilities                                  |
| `API.md`                 | Public methods, options, events, sink interface, lifecycle behavior              |
| `CONFIGURATION.md`       | Defaults, customization, precedence, reconfiguration                             |
| `FALLBACK_AND_FANOUT.md` | Routing modes, failure semantics, retries, duplicate-delivery behavior           |
| `DATABASE_SCHEMA.md`     | Schema, indexes, aggregation strategy, migration/versioning                      |
| `PERFORMANCE.md`         | Benchmarks, overhead, buffer tuning, limitations                                 |
| `TEST_PLAN.md`           | Unit, integration, concurrency, failure-injection, and stress tests              |
| `CHANGELOG.md`           | Changes by version                                                               |
| Package README files     | Package-specific purpose, dependencies, supported targets, examples              |

The documentation must describe actual implemented behavior. It must not promise features that exist only in a plan.

## 21. Decisions still requiring explicit validation

The following items must be settled by the agent during its inspection and design phase. They must not be guessed silently.

| Decision              | Required action                                                                       |
| --------------------- | ------------------------------------------------------------------------------------- |
| Minimum target        | Verify builds on .NET Framework 4.6.1 and other declared targets                      |
| .NET Standard support | Evaluate and test independently                                                       |
| NLog compatibility    | Select explicit adapter dependency/version strategy                                   |
| SQL provider          | Choose a provider and supported database engines, document the choice                 |
| Default sink          | Choose a safe default that works without external configuration                       |
| Fallback semantics    | Distinguish synchronous failure from delayed asynchronous failure                     |
| Fan-out policy        | Define whether each sink is independently attempted and whether fallback also applies |
| Aggregation key       | Specify the exact normalization and identity rules                                    |
| Aggregation lifetime  | Define expiry, eviction, and persistence semantics                                    |
| Counter scope         | Define process-local versus cross-process count                                       |
| Buffer overflow       | Choose and document a bounded policy                                                  |
| Retry behavior        | Define timeout, retry limit, backoff, and recovery                                    |
| Event reentrancy      | Prevent cycles without suppressing unrelated concurrent logs                          |
| Shutdown              | Define flush timeout, resource ownership, and pending-event behavior                  |
| Performance targets   | Measure baseline and establish realistic acceptance thresholds                        |
| Package release       | Validate package artifacts before any publication                                     |

### A correction to the earlier architecture

The earlier outline placed `FallbackGroup`, `BufferingWrapper`, and `AsyncWrapper` in a proposed NLog configuration and described that as the correct universal fallback implementation.

That should not be treated as the final design. Wrapper behavior, configuration syntax, and fallback activation depend on the actual NLog version and target implementation. More importantly, the core library must support fallback and fan-out even when NLog is not installed.

The final design must implement and test these semantics at the appropriate abstraction layer, rather than relying on a sample XML configuration to establish guarantees across all sinks.


## 22. Master prompt for Google Antigravity

Save the following as `ANTIGRAVITY_MASTER_PROMPT.md` and provide it to Antigravity along with this specification. This prompt instructs the agent to build the full project, not stop at architecture documentation or a skeleton.

MASTER PROMPT — EXCEPTIONMASTER

# MASTER PROMPT — EXCEPTIONMASTER

## Role

Act as a Senior .NET Framework Architect, Logging Infrastructure Engineer, Concurrency Specialist, NuGet Package Maintainer, and Test Engineer.

Build ExceptionMaster: a reusable, failure-contained exception logging framework for classic .NET Framework applications.

The implementation must be production-oriented, thoroughly tested, documented, and package-ready. Correctness and failure isolation take priority over clever abstractions or premature optimization.

## 1. Mandatory first phase: inspect, validate, plan

Before writing code:

1. Inspect the existing repository, project files, target frameworks, installed SDKs, test frameworks, dependency versions, build scripts, and Git status.

2. Identify any existing implementation or documentation related to ExceptionMaster.

3. Preserve existing work. Do not overwrite, delete, regenerate, or restructure unrelated projects.

4. Produce a requirements traceability matrix mapping every requirement in `ARCHITECTURE.md` to a planned component and test.

5. Identify unresolved design decisions, compatibility risks, and requirements that need clarification.

6. Verify which target frameworks and compiler versions are available.

7. Produce a phase-wise implementation plan with goals, expected outputs, dependencies, tests, and acceptance criteria.

Do not silently guess when an important decision affects API compatibility, persistence, concurrency, or failure semantics. Record the issue and ask for clarification when a safe decision cannot be made from the specification.

After completing the inspection and plan, proceed with implementation where decisions are sufficiently specified. Do not stop after generating the plan.

## 2. Core requirements

Implement all requirements in the canonical `ARCHITECTURE.md`.

The core package must:

* Have no NLog dependency.

* Expose no NLog types in its public API.

* Work without an external logging configuration file.

* Provide safe defaults.

* Support configurable sinks and custom sink implementations.

* Support fan-out and configurable fallback.

* Support exception aggregation and deduplication.

* Be thread-safe.

* Provide bounded asynchronous dispatch, buffering, batch flushing, and configurable flush intervals.

* Never propagate internal logging failures to the application caller.

* Provide fire-and-forget notifications with subscriber isolation and recursion protection.

* Include immutable or safely snapshotted exception context.

* Support explicit lifecycle management, flush, and bounded shutdown.

* Document unavoidable data-loss scenarios.

## 3. Framework compatibility

Target classic .NET Framework 4.6.1 or higher as the primary compatibility requirement, subject to build verification.

Evaluate .NET Standard 2.0 as a separate compatibility target. Do not claim support without building and testing the declared targets.

Avoid introducing .NET Core or modern .NET-only APIs into the legacy-targeted assembly.

Inspect compiler and package compatibility before choosing language features or dependencies.

## 4. Package boundaries

Implement separate projects for:

* AvaranFlow.Core

* AvaranFlow.Sinks.Console

* AvaranFlow.Sinks.File

* AvaranFlow.Sinks.Database

* AvaranFlow.NLog

* AvaranFlow.Core.Tests

* AvaranFlow.Sinks.Tests

* AvaranFlow.Concurrency.Tests

* AvaranFlow.FailureInjection.Tests

* AvaranFlow.Integration.Tests

Add executable samples for a classic console application, WinForms application, and custom sink.

Adjust the structure only when necessary for compatibility or maintainability, and document any deviation.

## 5. Context and aggregation

Include all required metadata:

Exception, Message, ExceptionType, StackTrace, FirstSeenUtc, LastSeenUtc, Count, Caller, Category, MachineName, ProcessName, ThreadId, AppVersion, CorrelationId, IsFatal, ExceptionKey, ProcessId, EnvironmentName, Source, inner-exception details, occurrence identity where needed, and schema version.

Use UTC timestamps. Preserve the original exception and full diagnostic details.

Define and test exception identity, aggregation windows, count semantics, periodic reporting, expiration, and bounded eviction.

Do not assume ConcurrentDictionary alone makes mutable aggregate objects thread-safe. Use a correct synchronization or immutable-state strategy and test coherent snapshots under concurrency.

Do not claim source line numbers are available without the necessary debugging symbols.

## 6. Sinks, fallback, and fan-out

Implement independently usable console, file, and database sinks.

The database connection must be optional and consumer-configurable. Document the supported SQL provider and database engines.

Support:

* SQL → file → console fallback.

* File → console fallback.

* Configurable fan-out to multiple independent sinks.

* Custom user-defined sinks.

* Timeouts, bounded retries, and recovery.

* File rotation and archive retention.

* SQL parameterization and safe credential handling.

* Buffer limits, batch processing, and flush intervals.

* Isolation of synchronous and asynchronous failures.

* Explicit delivery and duplicate-handling semantics.

Do not assume that enqueueing an event means it has been durably persisted.

A failed sink must not prevent attempts to deliver the event to other required sinks.

Prevent accidental duplicate deliveries caused by overlapping fallback and fan-out paths.

## 7. Optional NLog integration

Implement NLog as a separate optional adapter package.

Do not introduce an NLog dependency into AvaranFlow.Core.

Do not overwrite the consumer's NLog configuration, install an unexpected NLog.config, or shut down the host's global logger.

Declare and test supported NLog versions. Verify dependency resolution with a consumer that already references a compatible NLog version.

Handle missing, incompatible, misconfigured, or failing NLog integration safely.

Do not claim universal conflict-free compatibility with arbitrary NLog versions.

## 8. Fire-and-forget event

Expose an event or equivalent subscription API that publishes exception context asynchronously.

Requirements:

* The caller must not wait for subscriber execution.

* One subscriber failure must not affect other subscribers.

* Subscriber-generated exceptions must not create recursive notification chains.

* Unrelated concurrent logging must not be suppressed by a global reentrancy flag.

* Queue capacity and overflow must be bounded and documented.

* Subscription lifecycle and shutdown must be safe.

* Event delivery failure must not escape into the application.

## 9. Configuration and lifecycle

Implement Configure, ConfigureDefaults, documented automatic initialization, sink enablement, custom sink registration, explicit Flush, and bounded Shutdown.

Define configuration precedence, reconfiguration behavior, sink ownership, concurrent reconfiguration, and pending-event behavior during shutdown.

Never dispose host-owned resources accidentally.

## 10. Testing

Implement automated tests for all scenarios in `TEST_PLAN.md`.

At minimum, cover:

* Default and missing configuration.

* All metadata fields.

* Aggregation and timestamps.

* Counter accuracy under parallel load.

* Every fan-out and fallback combination.

* SQL failures and recovery.

* File failures and rotation.

* Console failures.

* NLog compatibility and isolation.

* Throwing custom sinks.

* Slow and throwing event subscribers.

* Event recursion and concurrent unrelated events.

* Buffer saturation and overflow.

* Reconfiguration and shutdown.

* Resource cleanup.

* Stress, memory, and performance behavior.

* NuGet package contents and consumer compatibility.

Use deterministic fakes and fault injection for unit tests. Use isolated integration infrastructure for real database and operating-system tests.

Do not weaken, skip, or delete tests simply to obtain a passing build. Explain environmental limitations explicitly.

## 11. NuGet and documentation

Produce valid NuGet packages for the supported projects.

Include:

* Correct package metadata.

* Target-framework declarations.

* Explicit dependencies.

* XML API documentation.

* Package README files.

* Installation and configuration instructions.

* SQL schema and indexes.

* Migration/versioning guidance.

* Release notes.

* Reproducible build and pack instructions.

* Executable consumer samples.

Do not publish to a public feed without explicit authorization.

## 12. Engineering rules

* Inspect before editing.

* Preserve unrelated existing code and user changes.

* Use the existing Git repository and conventions.

* Avoid unrelated refactoring.

* Never silently replace dependencies.

* Do not add unnecessary third-party packages.

* Do not claim thread safety without a defined synchronization model.

* Do not claim durability for events that remain buffered.

* Do not claim performance results without actual measurements.

* Do not report tests as passing unless they were executed and passed.

* Keep secrets out of source, tests, logs, and package artifacts.

* Treat logging infrastructure failures as a separate, non-recursive diagnostic path.

## 13. Required implementation phases

Phase 1 — Repository inspection, compatibility verification, and requirements traceability.

Phase 2 — Core API, immutable context, metadata capture, and aggregation.

Phase 3 — Sink contract, dispatcher, fan-out, and fallback semantics.

Phase 4 — Safe defaults, configuration, and lifecycle management.

Phase 5 — File and console sinks.

Phase 6 — Database sink, schema, batching, retries, and recovery.

Phase 7 — Optional NLog adapter and compatibility validation.

Phase 8 — Fire-and-forget notifications and recursion protection.

Phase 9 — Unit, integration, concurrency, and failure-injection tests.

Phase 10 — Benchmarks, memory tests, security review, and failure-path review.

Phase 11 — NuGet packaging, samples, API documentation, and release validation.

The phases may be reorganized when dependencies require it, but no requirement may be silently dropped.

## 14. Required completion report

At completion, provide:

1. Final repository tree.

2. Implemented public API and usage examples.

3. Supported target frameworks and dependency versions.

4. Requirements traceability matrix.

5. Tests executed, commands used, and actual results.

6. Tests not executed and the reason.

7. Benchmark methodology and measured results.

8. Known limitations and unresolved risks.

9. Package IDs, versions, and locally generated artifact paths.

10. Installation instructions for an existing classic .NET Framework application.

11. A list of modified and added files.

12. Git diff summary confirming that unrelated user changes were preserved.

## Definition of done

The project is complete only when the declared targets build, the required automated tests pass, failure paths are verified, package artifacts are validated, samples run, documentation matches actual behavior, and remaining limitations are explicitly reported.

Do not mark a feature complete merely because its classes or interfaces exist.

Use `ARCHITECTURE.md`, `TEST_PLAN.md`, and the requirements traceability matrix as the acceptance criteria. Do not substitute a simplified implementation for the complete specification.

## 23. Recommended execution sequence in Antigravity

Do not ask the agent to generate every file in a single unreviewed operation. Use the master prompt to establish the complete scope, then validate each implementation phase.

1. Inspect and plan. Review repository state, framework compatibility, requirements traceability, and unresolved decisions. Approve any decisions that materially affect the public API.

2. Build the core first. Implement the context, metadata capture, aggregation, public API, configuration, and sink contract. Run core tests before adding I/O.

3. Add dispatch and sinks. Implement fan-out, fallback, buffering, console, file, and database behavior. Test failures with fake sinks before relying on real infrastructure.

4. Add the optional adapter and event system. Verify NLog compatibility separately and test notification recursion, concurrency, and lifecycle behavior.

5. Validate the whole solution. Run all declared framework builds, automated tests, integration tests, stress tests, and benchmarks. Fix failures without weakening the requirements.

6. Package and document. Generate local NuGet artifacts, test them from a separate sample consumer, inspect dependencies, and produce the completion report.

## 24. Final acceptance checklist

## Project acceptance tracker

0/18

Core has no mandatory NLog dependency

Package and dependency inspection

Classic .NET Framework compatibility is verified

Build results for each declared target

Zero-configuration logging works safely

Default-configuration tests

SQL can be configured or disabled

Database configuration tests

SQL → file → console fallback works as specified

Failure-injection tests

Fan-out independently attempts enabled sinks

Dispatcher tests

Repeated errors update count and timestamps correctly

Concurrent aggregation tests

All requested diagnostic metadata is present

Context contract tests

Buffers and flush intervals are configurable

Queue and flush tests

Queue overflow and shutdown are bounded

Stress and lifecycle tests

Logging failures never escape to the application

Failure-injection tests

Fire-and-forget events cannot recurse indefinitely

Reentrancy and concurrency tests

NLog does not unexpectedly alter host configuration

Adapter integration tests

File rotation and retention are verified

File sink integration tests

SQL writes are parameterized and aggregation is safe

Database integration tests

Memory and performance behavior are measured

Benchmarks and stress tests

NuGet packages install in a separate consumer project

Package smoke tests

Documentation matches the delivered code

Release review

Reset trackerCopy checklist

The most important architectural conclusion: ExceptionMaster must own its framework-independent exception model, aggregation, fan-out, fallback, and failure-isolation policies. NLog should be an optional integration, not the foundation of the entire system. That is what allows the same library to be reused across legacy applications without forcing their logging configuration or dependencies to change.
