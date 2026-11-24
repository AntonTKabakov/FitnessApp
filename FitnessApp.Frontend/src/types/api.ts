export interface Profile { id: number; fullName: string; email: string; role: 'Admin' | 'User'; profilePictureUrl: string | null }
export interface Pagination { page: number; totalPages: number; totalCount: number }
export interface Catalog<T> { items: T[]; page: number; totalPages: number; total: number }
export interface Pass { id: number; name: string; price: number; maxVisits: number; validityDays: number; purchasedCount: number }
export interface Trainer { id: number; fullName: string; specialty: string; phone: string; checkinCount: number }
export interface Visit { id: number; customerId: number; displayName: string; customerPassId: number; passName: string; checkedInAt: string; trainerId: number | null; trainerName: string | null }
export interface Customer { id: number; displayName: string; email: string; activePasses: number }
export interface Membership { id: number; name: string; purchased: string; expires: string; remainingVisits: number; maxVisits: number; status: string; isActive: boolean; checkedInToday: boolean }
export interface CustomerDetails extends Customer { dateOfBirth: string | null; profilePictureUrl: string | null; passes: Membership[]; recentVisits: Visit[] }
export interface VisitOptions { passes: { id: number; customerName: string; name: string; remainingVisits: number; expires: string; checkedInToday: boolean }[]; trainers: SelectOption[] }
export interface SelectOption { value: string; text: string }
export interface Dashboard { todayVisits: number; trainerVisits: number; activePasses: number; customers: number; recentVisits: Visit[] }


