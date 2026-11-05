// Hand-written until #345 generates the API client; delete together with src/test/handlers/login.ts.

export interface LoginRequest {
  usernameOrEmail: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
}
