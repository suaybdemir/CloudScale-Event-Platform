import paramiko
import json

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"

def run_cmd(ssh, cmd):
    stdin, stdout, stderr = ssh.exec_command(cmd)
    return stdout.read().decode(), stderr.read().decode()

def diagnose():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        # 1. Container Status
        print("\n--- Docker PS (All) ---")
        out, err = run_cmd(ssh, "docker ps -a --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}'")
        print(out)
        
        # 2. Find Ingestion API and Processor
        out, _ = run_cmd(ssh, "docker ps --format '{{.Names}}'")
        containers = out.splitlines()
        
        ingestion_api = next((c for c in containers if "ingestion-api" in c), None)
        event_processor = next((c for c in containers if "event-processor" in c), None)
        nginx = next((c for c in containers if "nginx" in c), None)

        if nginx:
            print(f"\n--- Nginx Logs ({nginx}) ---")
            out, err = run_cmd(ssh, f"docker logs {nginx} --tail 50")
            print(out)
            print(err)

        if ingestion_api:
            # Existing logs...
            
            print("\n--- Live Alerts API Response ---")
            out, _ = run_cmd(ssh, f"docker exec {ingestion_api} curl -s http://localhost:8080/api/dashboard/alerts")
            print(out)
        
        for ep in [c for c in containers if "event-processor" in c]:
             print(f"\n--- Targeted Safeguard Logs ({ep}) ---")
             # Grep for late events, idempotency collisions and watchdog
             cmd = f"docker logs {ep} 2>&1 | grep -E 'LATE EVENT|IDEMPOTENCY|Watchdog|Re-hydrating'"
             out, _ = run_cmd(ssh, cmd)
             print(out)
             
             print(f"\n--- Recent Risk Evaluations ({ep}) ---")
             cmd = f"docker logs {ep} 2>&1 | grep 'Risk Evaluation' | tail -n 10"
             out, _ = run_cmd(ssh, cmd)
             print(out)

        # 3. Service Bus & Cosmos status
        for svc in ["servicebus-emulator", "cosmosdb-emulator"]:
            print(f"\n--- Inspect {svc} ---")
            out, _ = run_cmd(ssh, f"docker inspect {svc}")
            try:
                data = json.loads(out)
                if data:
                    print(f"Status: {data[0]['State']['Status']}")
                    if 'Health' in data[0]['State']:
                        print(f"Health: {data[0]['State']['Health']['Status']}")
            except:
                print(f"Container {svc} not found or inspect failed.")

        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    diagnose()
