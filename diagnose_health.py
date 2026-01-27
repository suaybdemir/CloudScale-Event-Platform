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
        
        print("\n--- Event Processor Logs (Tail 50) ---")
        stdin, stdout, stderr = ssh.exec_command("docker logs cloudscale-event-intelligence-platform-event-processor-1 --tail 50")
        print(stdout.read().decode())
        
        # Check current running status
        print("\n--- Container Status ---")
        stdin, stdout, stderr = ssh.exec_command("docker ps --format '{{.Names}}: {{.Status}}'")
        print(stdout.read().decode())

        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    diagnose()
