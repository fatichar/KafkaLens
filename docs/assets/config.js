const KAFKALENS_CONFIG = {
    github: {
        owner: 'fatichar',
        repo: 'KafkaLens',
        version: 'v0.9.6',
        assets: {
            windows: 'KafkaLens-0.9.6-win-x64-installer.exe',
            macos: 'KafkaLens-0.9.6-macos-arm64.zip',
            linux: 'KafkaLens-0.9.6-linux-x64.zip'
        }
    },
    umami: {
        scriptUrl: 'https://analytics.greenfit.in/script.js',
        websiteId: 'b9d3274a-0dbd-46d3-b1e9-762c04e02461'
    }
};

function getDownloadUrl(os) {
    const { owner, repo, version, assets } = KAFKALENS_CONFIG.github;
    const asset = assets[os];
    if (!asset) return null;
    return `https://github.com/${owner}/${repo}/releases/download/${version}/${asset}`;
}

if (typeof module !== 'undefined' && module.exports) {
    module.exports = { KAFKALENS_CONFIG, getDownloadUrl };
}
