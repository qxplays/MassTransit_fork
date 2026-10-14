# Experimental ActiveMQ publisher connections

Configure `h.PublisherConnectionCount(4)` in the host callback. Default 1 preserves the previous path. Values 2..8 opt into host-wide independent physical NMS connections, each with a FIFO executor and producer. Same group/correlation key routes to the same lane within this process; cross-process ordering is not guaranteed. Unkeyed messages use round robin. Temporary destinations and consumer ACKs stay on the original connection. Async failures forward to the existing supervisor; shutdown drains accepted commands before closing lane contexts.

Once NMS SendAsync starts, await its completion even when the caller cancels, to prevent overlapping operations on the same producer. No reliability benefit is claimed from NMS async-send settings: existing host configuration is reused. Payload, delivery mode and prepared message metadata are not rewritten by the pool.

Run `dotnet run --project tests/PublisherLaneChecks -f net8.0`. Mock checks exercise 1024 sends, per-key FIFO, per-producer exclusion, cancellation/fault isolation, original message metadata and shutdown of all created contexts. They do not prove wire TTL/priority behavior or financial recovery after disconnect/crash. A full fault/recovery scenario and broker wire-metadata checks are required before enabling in production.

Exploratory 400 TPS full-flow measurements observed completion 263.459 -> 373.785 TPS and publisher queue wait around 14.163 -> 0.11-0.14 ms. Both runs failed the complete latency criterion; binaries differed by exception forwarding. This is not a same-binary stable-capacity comparison, not a threefold improvement, and not proof of 1000 TPS. No package version or application dependency is changed by this draft.
