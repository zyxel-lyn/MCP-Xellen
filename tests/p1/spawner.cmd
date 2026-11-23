@echo off
rem Xellen P1 self-test spawner: two marker children, then stay alive.
rem /min: children must never open visible console windows (owner disturbance).
start /B "" cmd /c "title xellen-p1-test-childA & ping -n 600 127.0.0.1 > nul"
start /B "" cmd /c "title xellen-p1-test-childB & ping -n 600 127.0.0.1 > nul"
ping -n 6000 127.0.0.1 > nul