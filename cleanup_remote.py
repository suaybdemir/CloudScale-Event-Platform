import paramiko
import time

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"
REMOTE_DIR = "/home/suayb/cloudScale-event-intelligence-platform"

def run_cmd(ssh, command):
    print(f"nodes > {command}")
    stdin, stdout, stderr = ssh.exec_command(f"echo '{PASS}' | sudo -S -p '' bash -c '{command}'")
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    if out: print(out)
    if err: print(f"Error/Warning: {err}")
    return out, err

def cleanup():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        print("🧹 Keeping it clean... Force removing stuck containers...")
        
        # 1. Try graceful down first
        run_cmd(ssh, f"cd {REMOTE_DIR} && docker compose down --remove-orphans")
        run_cmd(ssh, "docker network prune -f")
        
        # 2. Aggressive kill list
        targets = [
            "name=cloudscale", 
            "name=servicebus-emulator", 
            "name=cosmosdb-emulator",
            "name=sqledge",
            "name=azurite"
        ]
        
        for t in targets:
            # Check if any exist first to avoid 'docker rm' usage error on empty list
            check_cmd = f"docker ps -a -q --filter {t}"
            ids, _ = run_cmd(ssh, check_cmd)
            if ids:
                print(f"   Killing {t} ({len(ids.split())} containers)...")
                run_cmd(ssh, f"docker rm -f {ids.replace(chr(10), ' ')}")
            else:
                print(f"   No containers found for {t}")
                
        print("♻️  Restarting Stack...")
        run_cmd(ssh, f"cd {REMOTE_DIR} && docker compose up -d")
        
        print("✅ Cleanup and Restart Triggered.")
        ssh.close()

    except Exception as e:
        print(f"❌ Error: {e}")

if __name__ == "__main__":
    cleanup()
