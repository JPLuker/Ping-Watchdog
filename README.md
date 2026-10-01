# Ping Watchdog

A lightweight Windows GUI for monitoring multiple IP addresses or hostnames and alerting only when failures become meaningful.

## Default monitoring behavior

- Ping interval: **2 seconds**
- Ping timeout: **1000 ms**
- Declare a host down after: **3 consecutive failures**
- Declare a host recovered after: **2 consecutive successes**
- A single failed ping only changes the host to **SUSPECT**
- The application alerts only once per outage, then alerts again when the host recovers

These values can be changed in the UI before monitoring starts.

## Usage

1. Enter one IP address or hostname per line.
2. Click **Start Monitoring**.
3. Watch the live status table:
   - **ONLINE** - responding normally
   - **SUSPECT** - one or more recent failures, but not enough to declare an outage
   - **OFFLINE** - failure threshold reached
4. Ping Watchdog shows an alert when a host is declared offline and another when it recovers.

## Windows EXE

GitHub Actions builds a self-contained 64-bit Windows executable.

Open the repository Actions tab, run **Build Windows EXE**, then download the PingWatchdog-win-x64 artifact.

Because the publish is self-contained, the target Windows PC does not need .NET installed.
