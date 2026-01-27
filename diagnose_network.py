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
        
        # Check connectivity from ingestion-api to cosmosdb-emulator
        print("\n--- Curl Cosmos DB from Ingestion API ---")
        cmd = "docker exec cloudscale-event-intelligence-platform-ingestion-api-1 curl -v -k https://cosmosdb-emulator:8081/_explorer/emulator.pem"
        stdin, stdout, stderr = ssh.exec_command(cmd)
        print(stdout.read().decode())
        print(stderr.read().decode())
        
        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    diagnose()
