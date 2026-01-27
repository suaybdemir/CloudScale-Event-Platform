import paramiko
import sys
import time

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"

def diagnose():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        print("⏳ Waiting for Cosmos DB to be healthy...")
        for i in range(20): # Wait up to 100 seconds
            stdin, stdout, stderr = ssh.exec_command("docker inspect --format='{{.State.Health.Status}}' cosmosdb-emulator")
            status = stdout.read().decode().strip()
            print(f"[{i*5}s] Status: {status}")
            
            if status == "healthy":
                print("✅ Cosmos DB is healthy!")
                break
            
            time.sleep(5)
            
        # Check logs again
        print("\n--- Ingestion API Logs (Latest) ---")
        stdin, stdout, stderr = ssh.exec_command("docker logs cloudscale-event-intelligence-platform-ingestion-api-1 --tail 20")
        print(stdout.read().decode())
        
        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    diagnose()
