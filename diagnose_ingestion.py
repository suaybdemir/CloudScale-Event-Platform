import paramiko
import sys

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"

def diagnose():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        # Check Ingestion API Logs (Latest 20)
        print("\n--- Ingestion API Logs (Latest 20) ---")
        stdin, stdout, stderr = ssh.exec_command("docker logs cloudscale-event-intelligence-platform-ingestion-api-1 --tail 20")
        print(stdout.read().decode())
        
        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    diagnose()
