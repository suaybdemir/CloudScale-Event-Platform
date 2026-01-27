import paramiko
import sys
import time

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"
REMOTE_PATH = "/home/suayb/cloudScale-event-intelligence-platform"

def run_cmd(ssh, cmd, description):
    print(f"\n--- {description} ({cmd}) ---")
    stdin, stdout, stderr = ssh.exec_command(cmd)
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
        
        # Restart cosmosdb and ingestion-api
        cmd = f"cd {REMOTE_PATH} && echo '{PASS}' | sudo -S docker compose restart cosmosdb-emulator ingestion-api"
        run_cmd(ssh, cmd, "Restarting Containers")
        
        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    fix()
