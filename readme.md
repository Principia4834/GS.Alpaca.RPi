# Green Swamp Server Alpaca Simulator
## About
Derived from OmniSim is an Alpaca telescope simulator. The goal is to be as compliant as possible with the Alpaca specification. By default it starts on localhost:32323.

The simulators are direct ports from the ASCOM Platform simulators over to .Net 8+. Once complete they are meant to be fully compatible with the platform versions. The configuration is achieved through a Blazor web UI. As the simulators are modernized they also will get a JSON API for configuration.

Settings, discovery, and logging are provided by the ASCOM Cross Platform libraries. The log and settings files can be found in the standard folders for the ASCOM Cross Platform project. From Driver Setup you can also set the logs to write to the console.

This supports Swagger / OpenAPI on the /swagger url.

Currently development is focused on proof of concept of an Alpaca compliant SkyWatcher ASCOM driver on RPi.