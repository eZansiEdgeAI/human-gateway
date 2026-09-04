/** Public authentication API contracts shared by the Edge and Relay. */

export interface UserView {
  id: string
  username: string
  displayName: string
  status: 'ACTIVE' | 'DISABLED' | string
  role: 'USER' | 'ADMIN' | string
  lastLoginAt?: string
  disabledAt?: string
  createdAt: string
  updatedAt?: string
}

export interface LoginResponse {
  token: string
  expiresAt: string
  user: UserView
}
