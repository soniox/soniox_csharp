# Soniox C# Client

## Dependencies

To install the neccesary dependecies navigate to root of the VS solution and run:
```
dotnet restore
```

## Examples

In order to run the examples you must set `SONIOX_API_KEY` environment variable.

```
export SONIOX_API_KEY=<API_KEY>
```

### Transcribe File Short

To transcribe shorter audio file navigate to `{SOLUTION_DIR}/examples/TranscribeFileShort` and run

```
dotnet run TranscribeFileShort.csproj
```

### Transcribe File Async

To transcribe longer audio file asynchronously navigate to `{SOLUTION_DIR}/examples/TranscribeFileAsync` and run

```
dotnet run TranscribeFileAsync.csproj
```