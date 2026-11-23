@echo off
rem Xellen P1 child-policy test: one marker child + one PLAIN child (no marker in cmdline).
rem /min: children must never open visible console windows (owner disturbance).
start /B "" cmd /c "title xellen-p1-test-childA & ping -n 600 127.0.0.1 > nul"
start /B "" cmd /c "ping -n 600 127.0.0.1 > nul"
ping -n 6000 127.0.0.1 > nul