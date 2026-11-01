// Hand-written until #347 generates the API types; delete together with src/test/handlers/login.ts.

export interface LoginRequest {
  usernameOrEmail: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
}
