import paramiko
from scp import SCPClient
import time

HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"
LOCAL_FILE = "load_test_10k.py"
REMOTE_FILE = f"/home/{USER}/{LOCAL_FILE}"

def run_test():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        # 1. Upload Test Script
        print(f"🚀 Uploading {LOCAL_FILE}...")
        with SCPClient(ssh.get_transport()) as scp:
            scp.put(LOCAL_FILE, REMOTE_FILE)
            
        # 2. Prepare Environment (Async IO needs libs)
        print("🔧 Preparing Python environment on server...")
        
        # Combined command to setup venv, install deps, and run
        # Use -u for unbuffered output to see live stats
        cmd = (
            f"sudo apt-get update > /dev/null 2>&1 && sudo apt-get install -y python3-venv python3-pip > /dev/null 2>&1 && "
            f"python3 -m venv stress_env && "
            f"stress_env/bin/pip install aiohttp > /dev/null 2>&1 && "
            f"stress_env/bin/python -u {REMOTE_FILE}"
        )
        
        print("🔥 Running Stress Test (Internal)...")
        # Using sudo -S for apt installs if needed
        full_cmd = f"echo '{PASS}' | sudo -S bash -c '{cmd}'"
        
        stdin, stdout, stderr = ssh.exec_command(full_cmd)
        
        # Stream Output
        while True:
            line = stdout.readline()
            if not line:
                break
            print(line.strip())
            
        exit_status = stdout.channel.recv_exit_status()
        if exit_status != 0:
            print(f"❌ Error: {stderr.read().decode()}")

        ssh.close()

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    run_test()
