function publish() {
    dotnet publish -r win-x64 -c Release --self-contained false
    dotnet publish -r win-x86 -c Release --self-contained false
}

write-host "publish"