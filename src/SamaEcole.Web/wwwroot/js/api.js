window.api = {
    baseUrl: '/api/v1',
    
    getHeaders() {
        const token = localStorage.getItem('token');
        const headers = {
            'Content-Type': 'application/json'
        };
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }
        return headers;
    },

    async request(endpoint, method = 'GET', body = null) {
        const options = {
            method,
            headers: this.getHeaders()
        };

        if (body) {
            options.body = JSON.stringify(body);
        }

        try {
            const response = await fetch(`${this.baseUrl}${endpoint}`, options);
            
            if (!response.ok) {
                // Parse standardized ErrorResponse (Volume 4 §0.4)
                let errorData;
                try {
                    errorData = await response.json();
                } catch {
                    throw new Error(`Erreur HTTP: ${response.status}`);
                }
                throw errorData;
            }

            if (response.status === 204) return null;
            return await response.json();
        } catch (error) {
            console.error("API call failed:", error);
            throw error;
        }
    },

    get(endpoint) { return this.request(endpoint, 'GET'); },
    post(endpoint, body) { return this.request(endpoint, 'POST', body); },
    put(endpoint, body) { return this.request(endpoint, 'PUT', body); },
    patch(endpoint, body) { return this.request(endpoint, 'PATCH', body); },
    delete(endpoint) { return this.request(endpoint, 'DELETE'); }
};
