import { ref } from 'vue';

const AD_SETTINGS_KEY = 'deearthx_show_ads';

// Read initial value from localStorage, default to true
function loadShowAds(): boolean {
    try {
        const saved = localStorage.getItem(AD_SETTINGS_KEY);
        if (saved !== null) {
            return saved === 'true';
        }
    } catch {
        // ignore
    }
    return true;
}

const showAds = ref<boolean>(loadShowAds());

export function getShowAds(): boolean {
    return showAds.value;
}

export function setShowAds(value: boolean): void {
    showAds.value = value;
    localStorage.setItem(AD_SETTINGS_KEY, String(value));
}

export { showAds };