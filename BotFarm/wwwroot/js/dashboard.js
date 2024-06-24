async function shutdown() {
    try {
        let response = await fetch('/admin/shutdown', {
            method: 'POST',
        });
        let result = await response.json();
        await showToast(result.message, true);
    }
    catch (error) {
        await showToast(error.message, false);
        console.error('Error:', error);
    }
}

async function getHealthData() {
    try {
        let response = await fetch('/health', {
            method: 'GET',
        });
        let result = await response.json();
        let uptime = result.entries.AppStats.data.Uptime;
        let memory = formatBytes(result.entries.MemoryCheck.data.AllocatedBytes);
        document.getElementById('uptime').innerText = uptime;
        document.getElementById('memory').innerText = memory;
    }
    catch (error) {
        await showToast(error.message, false);
        console.error('Error:', error);
    }
}

let getAppStatsButton = document.getElementById('getAppStatsButton');
getAppStatsButton.onclick = async () => {
    await getHealthData();
};

getHealthData();
