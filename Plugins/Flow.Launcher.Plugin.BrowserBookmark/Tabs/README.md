
## Chromiums

Mapping URLs to browsers' tabs requires the following in chromiums:  

'''
chrome.exe  --remote-debugging-port=9222
msedge.exe  --remote-debugging-port=9223
brave.exe   --remote-debugging-port=9224
opera.exe   --remote-debugging-port=9225
'''

It might be required to create a new profile:  

'''
chrome.exe --remote-debugging-port=9222 --user-data-dir="C:\Temp\ChromeProfile1"
'''

Then it's possible to take tabs this way:  

'''
curl http://localhost:9222/json
'''

## Firefox

Theoretically for Firefox it is required to run this:  

'''
firefox.exe --start-debugger-server 6000
'''

With this options enabled ("about:config"):  

'''
devtools.debugger.remote-enabled = true
devtools.debugger.prompt-connection = false
devtools.debugger.remote-websocket = true
devtools.debugger.remote-port = 6000
'''

But it does NOT work. Maybe gckodriver.exe is needed instead.  
Probably this would help but was not tried:  

'''
firefox.exe -P "debugprofile" --no-remote --start-debugger-server 6000
# ws://localhost:6000
# ws://localhost:6000/listTabs
'''
