import paramiko
import sys

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"

def diagnose_processor():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        # Get logs of Event Processor
        cmd = "docker logs cloudscale-event-intelligence-platform-event-processor-1 --tail 50"
        print(f"--- Running: {cmd} ---")
        
        stdin, stdout, stderr = ssh.exec_command(cmd)
        out = stdout.read().decode().strip()
        err = stderr.read().decode().strip()
        
        if out: print(out)
        if err: print(f"ERR: {err}")
            
        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    diagnose_processor()
