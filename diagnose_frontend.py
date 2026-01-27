import paramiko
import sys

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"
REMOTE_PATH = "/home/suayb/cloudScale-event-intelligence-platform"

def diagnose():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        # 1. Fetch main.jsx via external port
        print("\n--- Fetch main.jsx via Port 3001 ---")
        stdin, stdout, stderr = ssh.exec_command("curl -I http://localhost:3001/src/main.jsx")
        print(stdout.read().decode())
        
        # 2. Read package.json
        print("\n--- package.json ---")
        stdin, stdout, stderr = ssh.exec_command(f"cat {REMOTE_PATH}/src/CloudScale.Dashboard/package.json")
        print(stdout.read().decode())
        
        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    diagnose()
