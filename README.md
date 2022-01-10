# Barco G60 Projector Essentials Plugin (c) 2022

## Overview

This plugin is designed to work with Barco G60 projector controlled via Telnet (TCP/IP) or RS-232. For config information, see the [config snippets](##Configuration)

## Configuration

### RS-232

```json
{
  "key": "projector1",
  "uid": 4,
  "type": "barcog60",
  "name": "Projector",
  "group": "pluginDevices",
  "properties": {
    "control": {
      "controlPortDevKey": "processor",
      "controlPortNumber": 1,
      "method": "com",
      "comParams": {
        "protocol": "RS232",
        "baudRate": 115200,
        "hardwareHandshake": "None",
        "softwareHandshake": "None",
        "dataBits": 8,
        "parity": "None",
        "stopBits": 1
      }
    },
    "pollIntervalMs": 60000,
    "coolingTimeMs": 15000,
    "warmingTimeMs": 15000
  }
}
```

### TCP/IP

```json
{
  "key": "projector1",
  "uid": 4,
  "type": "barcog60",
  "name": "Projector",
  "group": "pluginDevice",
  "properties": {
    "control": {
      "method": "tcpIp",
      "tcpSshProperties": {
        "port": 3023,
        "address": "0.0.0.0",
        "username": "",
        "password": "",
        "autoReconnect": true,
        "autoReconnectIntervalMs": 5000,
        "bufferSize": 32768
      }
    },
    "pollIntervalMs": 60000,
    "coolingTimeMs": 15000,
    "warmingTimeMs": 15000
  }
}
```

### Bridge
```json
{
                "key": "plugin-bridge1",
                "uid": 3,
                "name": "Plugin Bridge",
                "group": "api",
                "type": "eiscApiAdvanced",
                "properties": {
                    "control": {
                        "tcpSshProperties": {
                            "address": "127.0.0.2",
                            "port": 0
                        },
                        "ipid": "B2",
                        "method": "ipidTcp"
                    },
                    "devices": [
                        {
                            "deviceKey": "projector1",
                            "joinStart": 1
                        }
                    ]
                }
            }
```


### SiMPL EISC Bridge Map

The tables below document the digital, analog, and serial joins of PepeprDash Essentials DisplayBase used to build the plugin.

#### Digitals
| dig-o (Input/Triggers)     | I/O | dig-i (Feedback)                               |
| -------------------------- | --- | ---------------------------------------------- |
| Power Off                  | 1   | Power Off feedback                             |
| Power On                   | 2   | Power On Feedback                              |
|                            | 3   | Is 2-way Display Feedback                      |
| Input Select 1 - HDMI 1    | 11  | Input 1 Feedback - HDMI 1                      |
| Input Select 2 - HDMI 2    | 12  | Input 2 Feedback - HDMI 2                      |
| Input Select 3 - DVI       | 13  | Input 3 Feedback - DVI                         |
| Input Select 4 - VGA       | 14  | Input 4 Feedback - VGA                         |
| Input Select 5 - SDI       | 15  | Input 5 Feedback - SDI                         |
| Input Select 6 - HD Base-T | 16  | Input 6 Feedback - HD Base-T                   |
|                            | 41  | Input 1 Button Visibility Feedback - HDMI 1    |
|                            | 42  | Input 2 Button Visibility Feedback - HDMI 2    |
|                            | 43  | Input 3 Button Visibility Feedback - DVI       |
|                            | 44  | Input 4 Button Visibility Feedback - VGA       |
|                            | 45  | Input 5 Button Visibility Feedback - SDI       |
|                            | 46  | Input 6 Button Visibility Feedback - HD Base-T |
|                            | 50  | Device Is Online Feedback                      |

#### Analogs
| an_o (Input/Triggers) | I/O | an_i (Feedback)         |
| --------------------- | --- | ----------------------- |
|                       | 1   | Socket Status Feedback  |
|                       | 2   | Monitor Status Feedback |
| Input Select          | 11  | Input Feedback          |


#### Serials
| serial-o (Input/Triggers) | I/O | serial-i (Feedback)               |
| ------------------------- | --- | --------------------------------- |
|                           | 1   | Device Name Feedback              |
|                           | 11  | Input 1 Name Feedback - HDMI 1    |
|                           | 12  | Input 2 Name Feedback - HDMI 2    |
|                           | 13  | Input 3 Name Feedback - DVI       |
|                           | 14  | Input 4 Name Feedback - VGA       |
|                           | 15  | Input 5 Name Feedback - SDI       |
|                           | 16  | Input 6 Name Feedback - HD Base-T |
