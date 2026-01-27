import paramiko
import sys
import time

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"
REMOTE_PATH = "/home/suayb/cloudScale-event-intelligence-platform"

def run_cmd(ssh, cmd, description):
    print(f"\n--- {description} ({cmd}) ---")
    stdin, stdout, stderr = ssh.exec_command(f"echo '{PASS}' | sudo -S bash -c '{cmd}'")
    while not stdout.channel.exit_status_ready():
        if stdout.channel.recv_ready():
            print(stdout.channel.recv(1024).decode(), end="")
    print(stdout.read().decode())
    print(stderr.read().decode())

def fix():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        # Stop and remove cosmosdb-emulator to clear data
        print("🧹 Clearing Data (Recreating Cosmos DB)...")
        run_cmd(ssh, f"cd {REMOTE_PATH} && docker compose stop cosmosdb-emulator && docker compose rm -f cosmosdb-emulator", "Removing Cosmos DB")
        
        # Start it fresh
        run_cmd(ssh, f"cd {REMOTE_PATH} && docker compose up -d cosmosdb-emulator", "Starting Cosmos DB")
        
        print("⏳ Waiting for Cosmos DB to initialize...")
        for i in range(30):
            stdin, stdout, stderr = ssh.exec_command("docker inspect --format='{{.State.Health.Status}}' cosmosdb-emulator")
            status = stdout.read().decode().strip()
            print(f"[{i*3}s] Status: {status}")
            if status == "healthy":
                print("✅ Cosmos DB is healthy.")
                break
            time.sleep(3)

        # Restart API to reconnect
        run_cmd(ssh, f"cd {REMOTE_PATH} && docker compose restart ingestion-api dashboard", "Restarting Services")
        
        print("⏳ Waiting for API to warm up and initialize containers...")
        time.sleep(10)

        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    fix()
