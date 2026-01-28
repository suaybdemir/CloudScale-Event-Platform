import paramiko
import sys

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"

def run_cmd(ssh, command):
    print(f"nodes > {command}")
    stdin, stdout, stderr = ssh.exec_command(f"echo '{PASS}' | sudo -S -p '' bash -c '{command}'")
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    if out: print(out)
    if err: print(f"Error/Warning: {err}")
    return out, err

def fetch_logs():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        containers = ["cosmosdb-emulator", "cloudscale-event-intelligence-platform-ingestion-api-1"]
        
        for c in containers:
            print(f"\n--- Logs for {c} ---")
            run_cmd(ssh, f"docker logs {c} 2>&1 | tail -n 50")
            print(f"--- End Logs {c} ---")

        ssh.close()

    except Exception as e:
        print(f"❌ Error: {e}")

if __name__ == "__main__":
    fetch_logs()
