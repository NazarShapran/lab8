import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 50 }, // below normal load
    { duration: '1m', target: 50 },
    { duration: '30s', target: 100 }, // normal load
    { duration: '1m', target: 100 },
    { duration: '30s', target: 200 }, // around the breaking point
    { duration: '1m', target: 200 },
    { duration: '30s', target: 300 }, // beyond the breaking point
    { duration: '1m', target: 300 },
    { duration: '30s', target: 0 }, // scale down. Recovery stage
  ],
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';

export default function () {
  const res = http.get(`${BASE_URL}/api/students`);
  check(res, { 'status was 200': (r) => r.status == 200 });
  sleep(1);
}
