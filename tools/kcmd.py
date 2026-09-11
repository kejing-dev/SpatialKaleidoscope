#!/usr/bin/env python3
"""Send one command to the in-app debug server through adb forward and print the reply."""
import socket, subprocess, sys
subprocess.run(["adb", "forward", "tcp:7777", "tcp:7777"], capture_output=True)
cmd = " ".join(sys.argv[1:]) or "status"
try:
    s = socket.create_connection(("127.0.0.1", 7777), timeout=90)
    s.sendall((cmd + "\n").encode())
    s.settimeout(90)
    data = b""
    while True:
        chunk = s.recv(4096)
        if not chunk: break
        data += chunk
        if b"\n" in data: break
    s.close()
    print(data.decode(errors="replace").strip())
except Exception as e:
    print("ERR", e); sys.exit(1)
