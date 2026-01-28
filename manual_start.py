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

def manual_start():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        print("🔍 Checking docker-compose.yml content...")
        run_cmd(ssh, "cd /home/suayb/cloudScale-event-intelligence-platform && cat docker-compose.yml | grep -A 5 cosmosdb-emulator")
        
        print("🗑️  Removing old cosmosdb-emulator...")
        run_cmd(ssh, "docker rm -f cosmosdb-emulator")
        
        print("▶️ Recreating cosmosdb-emulator with new config...")
        run_cmd(ssh, "cd /home/suayb/cloudScale-event-intelligence-platform && docker compose up -d cosmosdb-emulator")
        
        print("▶️ Starting other services...")
        run_cmd(ssh, "cd /home/suayb/cloudScale-event-intelligence-platform && docker compose up -d")

        ssh.close()

    except Exception as e:
        print(f"❌ Error: {e}")

if __name__ == "__main__":
    manual_start()
