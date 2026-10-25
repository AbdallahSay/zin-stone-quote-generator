/**
 * ZIN STONE Quotation System - Client Configuration
 * Supports Local Development, Netlify Frontend, and Render Backend.
 */
const CONFIG = {
    API_BASE_URL: window.ENV_API_URL || (
        window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1'
            ? 'http://127.0.0.1:5000'
            : (window.location.hostname.endsWith('onrender.com') 
                ? window.location.origin 
                : 'https://zin-stone-api.onrender.com')
    )
};
