# TBot 4.0

First release with a unified repo: C# (TBot) and Go (ogamed) in one state.

## Contents
- Base: TBot-5a (fork of ronnie32/TBot v3.5.0-beta5) with all server patches
- Brain transport chain, switchable (`Brain.Transports.UseTransportChain`, `CompactSlotPlan`)
- AutoFarm: probe cargo in the capacity check (from ronnie32 v3.5.1-beta2)
- `ogamed/`: Go live code (cmd/ogamed-tbot)
- `build-all.sh`: builds ogamed + TBot and restarts the service
- Anonymised config template under `config/`

## Update on the server
    cd /home/tbot/TBot4.0 && git pull && ./build-all.sh
