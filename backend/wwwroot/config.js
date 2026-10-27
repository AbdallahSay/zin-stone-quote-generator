/**
 * ZIN STONE Quotation System - Client Configuration
 * Supports Local Development, Vercel Frontend, and MonsterASP.NET Backend.
 */
const CONFIG = {
    API_BASE_URL: window.ENV_API_URL || (
        window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1'
            ? 'http://127.0.0.1:5000'
            : (window.location.hostname === 'zain-qoute.runasp.net'
                ? window.location.origin 
                : 'https://zain-qoute.runasp.net')
    )
};
