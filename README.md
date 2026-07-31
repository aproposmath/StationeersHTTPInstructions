# HTTP Instructions for IC10

Adds four HTTP instructions to Stationeers IC10. Listener instructions bind to all network interfaces, so only use trusted IC10 programs and networks.

| Instruction | Purpose |
| --- | --- |
| `http_get` | Fetch data **from another server** |
| `http_post` | Send data **to another server** |
| `http_on_post` | Send data **to IC10** with an HTTP POST |
| `http_on_get` | Serve data **from IC10** with an HTTP GET |

## Installation

Two possibilities:
- Download the latest release from [GitHub](https://github.com/aproposmath/StationeersHTTPInstructions/releases) and copy the `HTTPInstructions.dll` to <Stationeers install dir>/BepInEx/plugins
- Run these commands in the StationeersLaunchPad console:
```
slp repos add github.com/aproposmath/StationeersHTTPInstructions
slp repomods add StationeersHTTPInstructions
```

## Argument types

#### `<url>`
- The url to fetch or post to
- Single quotes are optional
- Templates are supported: `https://example.com/${r0}/?page=${someIC10Alias}`

#### `<input_data>`
- A register/alias name sends its numeric value as JSON.
- To send a JSON value or object, enclose it in single quotes. Templates are supported: `'{"temperature":${r0}, "status":"${r1}"}'`

#### `<output_data>`
- Either a register/alias name (the response must be a single number or string), or a single-quoted JSON object mapping register/alias names to JSON paths. Templates are supported in JSON paths.
- The special value `"$success"` sets its output register to `1` for a successful HTTP status and `0` otherwise.
- If "$success" is present, the other outputs are unchanged on failure
- If "$success" is NOT present, all outputs are set to `NaN` on failure
- Strings are packed with `STR()`. A `[start:end]` suffix selects part of a string. Both, `start` and `end` are optional. If `start` is omitted, it defaults to `0`. If `end` is omitted, the next 6 characters are selected (or fewer at the end of the string). Max 6 chars are extracted. If the range is invalid, the output is set to `NaN`.

Examples:

## `http_get <url> [<output_data>]`

If `output_data` is given, this instruction yields until the request is done (at least once).

```ic10
# Fetch a number from a server and store it in r9, this will yield until the request is complete
http_get 'http://127.0.0.1:8000/number' r9

# expected response: {"data": {"temperatures":[23.5, 25.3]},"name":"Stationeers"}
# result: r0=23.5, r1="Station", r2=1
http_get 'http://127.0.0.1:8000/data' '{"r0":"data.temperatures[0]","r1":"name[0:6]","r2":"$success"}'

# fire and forget, no yield
http_get 'http://127.0.0.1:8000/trigger'
```

## `http_post <url> <input_data> [<output_data>]`

If `output_data` is given, this instruction yields until the request is done (at least once).

```ic10
# fire and forget, no yield
http_post 'http://127.0.0.1:8000/readings' '"${r0}"'

# wait until done, store success in r15
alias aliasTemperature r1
http_post 'http://127.0.0.1:8000/data' '{"temperature": ${aliasTemperature}, "pressure": ${r2}}' '{"r15":"$success"}'
```

## `http_on_post <port> <path> <output_data>`

Register a listener on given port and path. Once a POST request is received, the next execution of `http_on_post` will set `success` to `1` and the other outputs. Subsequent executions will set `success` to `0` until a new POST is received.

```ic10
http_on_post 8080 /sensor '{"r0":"temperature","r1":"$success"}'
http_on_post 8080 /trigger '{"r2":"$success"}' # r2 becomes 1 on a POST to /trigger, 0 otherwise
```

```sh
curl -X POST http://127.0.0.1:8080/sensor -d '{"temperature":23.5}'
curl -X POST http://127.0.0.1:8080/trigger
```

## `http_on_get <port> <path> <input_data>`

Register a listener on given port and path and serve the given data on GET requests. Subsequent executions of `http_on_get` will update the served data.

```ic10
http_on_get 8080 /status '{"pressure":${r0}}'
```

```sh
# both calls will return the same result
curl http://127.0.0.1:8080/status
curl http://127.0.0.1:8080/status
```
