import paramiko
import sys

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"

def diagnose_sb():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        # Check Config.json existence
        print("--- Checking Config.json ---")
        ssh.exec_command(f"ls -l /home/{USER}/cloudScale-event-intelligence-platform/Config.json")
        
        # Get logs of Service Bus Emulator
        cmd = "docker logs servicebus-emulator --tail 100"
        print(f"\n--- Running: {cmd} ---")
        
        stdin, stdout, stderr = ssh.exec_command(cmd)
        out = stdout.read().decode().strip()
        err = stderr.read().decode().strip()
        
        if out: print(out)
        if err: print(f"ERR: {err}")
            
        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    diagnose_sb()
